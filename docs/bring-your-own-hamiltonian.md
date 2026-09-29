---
layout: default
title: Bring Your Own Hamiltonian
---

# Bring Your Own Hamiltonian

**Plug external quantum chemistry packages into the library** — the built-in chemistry stack ships with empirical Hamiltonians for small molecules (H₂, H₂O, LiH), but the Hamiltonian builders and the VQE layers below them accept externally computed data. This page maps out where to plug in, depending on what your external tool produces.

The library deliberately has **no dependency on any chemistry package**. Instead it exposes typed seams at every level of the chemistry pipeline:

```
Molecule geometry ──► Integrals ──► Fermionic Hamiltonian ──► Qubit (Pauli) Hamiltonian ──► VQE / ADAPT-VQE / Trotter evolution
      ▲                  ▲                  ▲                        ▲
  providers,        IntegralProvider   FermionHamiltonian     PauliHamiltonian
  XYZ/PDB/SMILES    (PySCF, Psi4…)     (OpenFermion-style)    (already mapped)
```

## Which entry point do I need?

| You have... | Plug in at | API |
|---|---|---|
| Molecular integrals from PySCF / Psi4 / NWChem | Integral level | `MolecularIntegrals` → `MolecularHamiltonian.buildFromIntegrals`, or an `IntegralProvider` → `MolecularHamiltonian.buildWithMapping` |
| An FCIDUMP file (standard interchange format) | Integral level, after parsing it yourself | `Molecule.fromFciDumpFileTask` reads only the header (see below) |
| Second-quantized fermionic operators | Fermion level | `FermionMapping.FermionHamiltonian` + Jordan-Wigner / Bravyi-Kitaev |
| Already-mapped Pauli terms (e.g. OpenFermion / Qiskit Nature output) | Pauli level | `TrotterSuzuki.PauliHamiltonian` → `AdaptVqe.run`, `Primitives.observe`, Trotter evolution |
| Molecule structures in external databases / formats | Data level | `IMoleculeDatasetProvider`, `IGeometryProvider`, XYZ/MOL2/PDB/SMILES parsers |

## 1. Integral providers (PySCF, Psi4, NWChem, ...)

An integral provider is a plain function, no interface ceremony: `Molecule -> Result<MolecularIntegrals, string>`. `MolecularHamiltonian.buildWithMapping` calls it and maps the result to qubits with Jordan-Wigner or Bravyi-Kitaev:

```fsharp
open FSharp.Azure.Quantum.QuantumChemistry

// Molecule -> Result<MolecularIntegrals, string>
let myProvider : IntegralProvider =
    fun molecule ->
        // Call your external package here (pythonnet, subprocess, REST, file...).
        // This stand-in returns the bundled H2 / STO-3G integrals.
        let reference = MolecularHamiltonian.h2Sto3gIntegrals
        Ok {
            NumOrbitals = 2
            NumElectrons = 2
            NuclearRepulsion = 0.713696
            OneElectron = { NumOrbitals = 2; Integrals = reference.OneElectron.Integrals }  // float[,]
            TwoElectron = { NumOrbitals = 2; Integrals = reference.TwoElectron.Integrals }  // float[,,,]
            ReferenceEnergy = Some -1.116765                                                // Hartree-Fock
        }

let molecule = Molecule.createH2 0.7414

match MolecularHamiltonian.buildWithMapping molecule MolecularHamiltonian.JordanWigner (Some myProvider) with
| Ok hamiltonian -> printfn "%d qubits, %d Pauli terms" hamiltonian.NumQubits hamiltonian.Terms.Length
| Error err -> eprintfn "Hamiltonian failed: %s" err.Message
```

If you already hold the integrals, skip the provider and call `MolecularHamiltonian.buildFromIntegrals`. It returns the qubit Hamiltonian together with the nuclear repulsion energy, which you add to the VQE result. `FermionMapping.ChemistryVQE.run` runs a UCCSD VQE on that Hamiltonian:

```fsharp
open FSharp.Azure.Quantum.QuantumChemistry.FermionMapping
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends

match MolecularHamiltonian.buildFromIntegrals MolecularHamiltonian.h2Sto3gIntegrals MolecularHamiltonian.JordanWigner with
| Error err -> eprintfn "Hamiltonian failed: %s" err.Message
| Ok (hamiltonian, nuclearRepulsion) ->
    let vqeConfig : ChemistryVQE.ChemistryVQEConfig =
        { Hamiltonian = fromQaoaHamiltonian hamiltonian
          Ansatz = ChemistryVQE.UCCSD(2, 4)   // 2 electrons, 4 spin orbitals (= qubits)
          MaxIterations = 100
          Tolerance = 1e-4
          UseHFInitialState = true
          Backend = LocalBackendFactory.createUnified ()  // or a cloud backend
          ProgressReporter = None }

    match ChemistryVQE.run vqeConfig |> Async.RunSynchronously with
    | Ok result -> printfn "Total energy: %.6f Ha" (result.Energy + nuclearRepulsion)  // ≈ -1.137 Ha
    | Error err -> eprintfn "VQE failed: %s" err.Message
```

**`SolverConfig.IntegralProvider`:** `SolverConfig` (used by `GroundStateEnergy.estimateEnergy`) also has an `IntegralProvider` field, but the current version of `estimateEnergy` does not read it. With `Method = GroundStateMethod.VQE`, molecules recognised as H₂, H₂O or LiH return tabulated reference energies, and other molecules use the empirical Hamiltonian. Build the Hamiltonian as shown above until that field is honoured.

**Requirements for the integrals** (see the full preconditions block in `Solvers/Quantum/QuantumChemistry.fs`):

- Molecular-orbital (MO) basis, not atomic-orbital — transform first (`h_MO = Cᵀ h_AO C`, use `ao2mo` in PySCF).
- Two-electron integrals in **chemist notation** `(pq|rs)`. PySCF uses this by default; Psi4 returns physicist notation `<pr|qs>` and needs conversion.
- Energies in Hartree; `Molecule` positions are in Angstroms.
- ≤ 10 spatial orbitals (the spin-orbital expansion doubles this to 20 qubits). `buildFromIntegrals` returns an `Error` for anything larger, whatever the backend. Larger molecules need an active-space selection in your external package before handing over the integrals.

**Working example**: [examples/DrugDiscovery/PySCFIntegration.fsx](../examples/DrugDiscovery/PySCFIntegration.fsx) implements a PySCF-backed provider via pythonnet, including basis-set selection and validation against the Hartree-Fock reference energy. [examples/Chemistry/H2_UCCSD_VQE_Example.fsx](../examples/Chemistry/H2_UCCSD_VQE_Example.fsx) runs the UCCSD VQE shown above.

## 2. FCIDUMP files

Most quantum chemistry packages (Molpro, PySCF, Q-Chem, ...) export FCIDUMP. The library can read the file's header:

```fsharp
open System.Threading

let fromFile = Molecule.fromFciDumpFileTask "h2.fcidump" CancellationToken.None   // Task<Result<Molecule, QuantumError>>
```

`fromFciDumpFileTask` currently parses only the header (`NORB`, `NELEC`, `MS2`, ...). The integral values in the file are not read, and FCIDUMP files carry no 3D geometry, so the resulting `Molecule` has placeholder atoms and records the orbital and electron counts in its metadata. Energy calculations on that `Molecule` therefore do not use the file's integrals. To use them, parse the integral lines yourself into a `MolecularIntegrals` value and call `MolecularHamiltonian.buildFromIntegrals` (section 1). The `quantumChemistry` computation expression accepts an FCIDUMP path (`molecule_from_fcidump`) with the same limitation.

## 3. Fermionic Hamiltonians (second quantization)

If your external tool produces creation/annihilation operator terms (OpenFermion's `FermionOperator`, for example), build a `FermionHamiltonian` and map it to qubits with your choice of transform:

```fsharp
open System.Numerics

let fermionH : FermionHamiltonian = {
    NumOrbitals = 4   // spin orbitals
    Terms = [
        { Coefficient = Complex(-1.2524, 0.0)
          Operators = [ { OrbitalIndex = 0; OperatorType = Creation }
                        { OrbitalIndex = 0; OperatorType = Annihilation } ] }
        // ... one- and two-body terms from your package
    ]
}

// Jordan-Wigner: simple, locality-preserving for 1D
let qubitH = JordanWigner.transform fermionH

// or Bravyi-Kitaev: lower gate depth for larger systems
let qubitH' = BravyiKitaev.transform fermionH

// Feed VQE/QAOA infrastructure
let problemH = toQaoaHamiltonian qubitH
```

`qubitH` is a `QubitHamiltonian`, the input `ChemistryVQE.run` takes (section 1); `toQaoaHamiltonian` converts it to the `ProblemHamiltonian` form used by the QAOA and Hamiltonian-builder code.

## 4. Pauli Hamiltonians (already mapped)

If the external stack already did the fermion-to-qubit mapping, hand the Pauli sum directly to the algorithm layer via `TrotterSuzuki.PauliHamiltonian`. `Operators.[i]` is the Pauli on qubit i:

```fsharp
open FSharp.Azure.Quantum.Algorithms

let term (ops: char[]) (coeff: float) : TrotterSuzuki.PauliString =
    { Operators = ops; Coefficient = Complex(coeff, 0.0) }

// H = -1.05 II + 0.39 ZI + 0.39 IZ - 0.01 ZZ + 0.18 XX  (H2, STO-3G, mapped)
let hamiltonian : TrotterSuzuki.PauliHamiltonian =
    { NumQubits = 2
      Terms = [ term [| 'I'; 'I' |] -1.05
                term [| 'Z'; 'I' |]  0.39
                term [| 'I'; 'Z' |]  0.39
                term [| 'Z'; 'Z' |] -0.01
                term [| 'X'; 'X' |]  0.18 ] }
```

Everything downstream consumes this type:

- **ADAPT-VQE**: `AdaptVqe.run backend hamiltonian pool numQubits config` — grows a problem-tailored ansatz; see [examples/Algorithms/AdaptVqe.fsx](../examples/Algorithms/AdaptVqe.fsx).
- **Expectation values**: `Primitives.observe backend circuit hamiltonian` runs a circuit and returns ⟨H⟩; `Primitives.expectation hamiltonian state` evaluates it on a state you already have.
- **Time evolution**: `TrotterSuzuki.synthesizeHamiltonianEvolution` — circuit for e^(−iHt).

The chemistry QPE path (`GroundStateMethod.QPE`) does not take a Pauli Hamiltonian: it builds the empirical Hamiltonian itself and currently estimates the energy with a simplified single-phase-gate proxy rather than Trotterised phase estimation, so use VQE or ADAPT-VQE for energies.

For small dense matrices there is also `TrotterSuzuki.decomposeMatrixToPauli` (and `decomposeDiagonalMatrixToPauli`), which computes the Pauli decomposition for you.

A `ProblemHamiltonian` from section 1 or 3 converts to this form in a few lines:

```fsharp
open FSharp.Azure.Quantum.Core

let toPauliHamiltonian (h: QaoaCircuit.ProblemHamiltonian) : TrotterSuzuki.PauliHamiltonian =
    let letter op =
        match op with
        | QaoaCircuit.PauliI -> 'I'
        | QaoaCircuit.PauliX -> 'X'
        | QaoaCircuit.PauliY -> 'Y'
        | QaoaCircuit.PauliZ -> 'Z'

    { NumQubits = h.NumQubits
      Terms =
        [ for t in h.Terms ->
            let ops = Array.create h.NumQubits 'I'
            Array.iter2 (fun q op -> ops.[q] <- letter op) t.QubitsIndices t.PauliOperators
            { Operators = ops; Coefficient = Complex(t.Coefficient, 0.0) } ] }
```

## 5. Molecule data and geometry providers

To source molecular *structures* (rather than Hamiltonians) from external systems, implement the provider interfaces in `Data/ChemistryDataProviders.fs`:

- `IMoleculeDatasetProvider` / `IMoleculeDatasetProviderAsync` — molecule databases (query by name, list, describe)
- `IGeometryProvider` / `IGeometryProviderAsync` — 3D geometry generation or lookup
- `IElementProvider` — element metadata (defaults to the built-in periodic table)
- File parsers for XYZ, MOL2, PDB and SMILES are built in (`MoleculeFormats`, `SmilesDataProviders`)

<!-- fragment -->
```fsharp
let mol = Molecule.fromProvider myDatasetProvider "caffeine"                   // your IMoleculeDatasetProvider
let mol' = Molecule.fromXyzFileTask "conformer42.xyz" CancellationToken.None   // your files
```

## Scale honestly

The built-in VQE/QPE path is validated on small molecules (H₂, H₂O, LiH), and the chemistry Hamiltonian builders refuse anything wider than 20 qubits (`Types.NisqPracticalQubits`). That is a fixed limit on every backend, not the local simulator's memory limit. "Bring your own Hamiltonian" does not remove that ceiling — it removes the *accuracy* ceiling (empirical vs research-grade integrals) and lets your external package do what it is good at (integrals, active-space selection, orbital localization) while this library does what it is good at (typed circuit construction, backend routing, error mitigation, Azure Quantum execution). For molecules beyond the qubit budget, reduce to an active space externally before handing over. A Pauli Hamiltonian passed straight to `AdaptVqe.run` or `Primitives.observe` is limited by the backend instead.

## See also

- [examples/DrugDiscovery/PySCFIntegration.fsx](../examples/DrugDiscovery/PySCFIntegration.fsx) — complete integral-provider implementation
- [examples/Chemistry/H2_UCCSD_VQE_Example.fsx](../examples/Chemistry/H2_UCCSD_VQE_Example.fsx) — UCCSD VQE on the H₂ integrals
- [examples/Algorithms/AdaptVqe.fsx](../examples/Algorithms/AdaptVqe.fsx) — hand-built `PauliHamiltonian` into ADAPT-VQE
- [Error Mitigation](error-mitigation) — improving results on noisy backends
- [Backend Switching](backend-switching) — running the same Hamiltonian locally vs on Azure Quantum hardware
