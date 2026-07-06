---
layout: default
title: Bring Your Own Hamiltonian
---

# Bring Your Own Hamiltonian

**Plug external quantum chemistry packages into the library** — the built-in chemistry stack ships with empirical integrals for small molecules (H₂, H₂O, LiH), but every solver accepts externally computed data. This page maps out where to plug in, depending on what your external tool produces.

The library deliberately has **no dependency on any chemistry package**. Instead it exposes typed seams at every level of the chemistry pipeline:

```
Molecule geometry ──► Integrals ──► Fermionic Hamiltonian ──► Qubit (Pauli) Hamiltonian ──► VQE / QPE / Trotter evolution
      ▲                  ▲                  ▲                        ▲
  providers,        IntegralProvider   FermionHamiltonian     PauliHamiltonian
  XYZ/PDB/SMILES    (PySCF, Psi4…)     (OpenFermion-style)    (already mapped)
```

## Which entry point do I need?

| You have... | Plug in at | API |
|---|---|---|
| Molecular integrals from PySCF / Psi4 / NWChem | Integral level | `IntegralProvider` in `SolverConfig` |
| An FCIDUMP file (standard interchange format) | File level | `Molecule.fromFciDumpFileTask` |
| Second-quantized fermionic operators | Fermion level | `FermionMapping.FermionHamiltonian` + Jordan-Wigner / Bravyi-Kitaev |
| Already-mapped Pauli terms (e.g. OpenFermion / Qiskit Nature output) | Pauli level | `TrotterSuzuki.PauliHamiltonian` → `AdaptVqe.run`, QPE, Trotter evolution |
| Molecule structures in external databases / formats | Data level | `IMoleculeDatasetProvider`, `IGeometryProvider`, XYZ/MOL2/PDB/SMILES parsers |

## 1. Integral providers (PySCF, Psi4, NWChem, ...)

`GroundStateEnergy.estimateEnergy` and the `quantumChemistry` builder accept a custom integral provider — a plain function, no interface ceremony:

```fsharp
open FSharp.Azure.Quantum.QuantumChemistry

// Molecule -> Result<MolecularIntegrals, string>
let myProvider : IntegralProvider =
    fun molecule ->
        // call your external package (pythonnet, subprocess, REST, file...)
        Ok {
            NumOrbitals = 2
            NumElectrons = 2
            NuclearRepulsion = 0.7137
            OneElectron = { NumOrbitals = 2; Integrals = h_pq }     // float[,]
            TwoElectron = { NumOrbitals = 2; Integrals = g_pqrs }   // float[,,,]
            ReferenceEnergy = Some hartreeFockEnergy
        }

let config =
    { Method = GroundStateMethod.VQE
      Backend = None                        // None = LocalBackend
      MaxIterations = 100
      Tolerance = 1e-6
      InitialParameters = None
      ProgressReporter = None
      ErrorMitigation = None
      IntegralProvider = Some myProvider }  // <-- your external integrals

let! result = GroundStateEnergy.estimateEnergy molecule config
```

**Requirements for the integrals** (see the full preconditions block in `Solvers/Quantum/QuantumChemistry.fs`):

- Molecular-orbital (MO) basis, not atomic-orbital — transform first (`h_MO = Cᵀ h_AO C`, use `ao2mo` in PySCF).
- Two-electron integrals in **chemist notation** `(pq|rs)`. PySCF uses this by default; Psi4 returns physicist notation `<pr|qs>` and needs conversion.
- Energies in Hartree; `Molecule` positions are in Angstroms.
- ≤ 10 spatial orbitals for local simulation (spin-orbital expansion doubles this to 20 qubits). Larger molecules need an active-space selection in your external package before handing over the integrals.

**Working example**: [examples/DrugDiscovery/PySCFIntegration.fsx](../examples/DrugDiscovery/PySCFIntegration.fsx) implements a complete PySCF-backed provider via pythonnet, including basis-set selection and validation against the Hartree-Fock reference energy.

## 2. FCIDUMP files

Most quantum chemistry packages (Molpro, PySCF, Q-Chem, ...) export FCIDUMP — use it as a zero-code interchange path:

```fsharp
let! result = Molecule.fromFciDumpFileTask "h2.fcidump" ct   // Task<Result<Molecule, QuantumError>>
```

Note: FCIDUMP files carry orbital/electron information but usually no 3D geometry, so the resulting `Molecule` has placeholder atoms. Use this path for energy calculations, not for geometry-dependent workflows. The `quantumChemistry` computation expression also accepts an FCIDUMP path directly.

## 3. Fermionic Hamiltonians (second quantization)

If your external tool produces creation/annihilation operator terms (OpenFermion's `FermionOperator`, for example), build a `FermionHamiltonian` and map it to qubits with your choice of transform:

```fsharp
open System.Numerics
open FSharp.Azure.Quantum.QuantumChemistry.FermionMapping

let fermionH : FermionHamiltonian = {
    NumOrbitals = 4
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

## 4. Pauli Hamiltonians (already mapped)

If the external stack already did the fermion-to-qubit mapping, hand the Pauli sum directly to the algorithm layer via `TrotterSuzuki.PauliHamiltonian`:

```fsharp
open System.Numerics
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
- **Time evolution**: `TrotterSuzuki.synthesizeHamiltonianEvolution` — circuit for e^(−iHt).
- **Phase estimation**: the QPE modules take the same Pauli form for eigenvalue extraction.

For small dense matrices there is also `TrotterSuzuki.decomposeMatrixToPauli` (and `decomposeDiagonalMatrixToPauli`), which computes the Pauli decomposition for you.

## 5. Molecule data and geometry providers

To source molecular *structures* (rather than Hamiltonians) from external systems, implement the provider interfaces in `Data/ChemistryDataProviders.fs`:

- `IMoleculeDatasetProvider` / `IMoleculeDatasetProviderAsync` — molecule databases (query by name, list, describe)
- `IGeometryProvider` / `IGeometryProviderAsync` — 3D geometry generation or lookup
- `IElementProvider` — element metadata (defaults to the built-in periodic table)
- File parsers for XYZ, MOL2, PDB and SMILES are built in (`MoleculeFormats`, `SmilesDataProviders`)

```fsharp
let mol = Molecule.fromProvider myDatasetProvider "caffeine"   // your database
let mol' = Molecule.fromXyzFileTask "conformer42.xyz" ct       // your files
```

## Scale honestly

The built-in VQE/QPE path is validated on small molecules (H₂, H₂O, LiH) and capped at 20 qubits on the local simulator. "Bring your own Hamiltonian" does not remove that ceiling — it removes the *accuracy* ceiling (empirical vs research-grade integrals) and lets your external package do what it is good at (integrals, active-space selection, orbital localization) while this library does what it is good at (typed circuit construction, backend routing, error mitigation, Azure Quantum execution). For molecules beyond the qubit budget, reduce to an active space externally before handing over.

## See also

- [examples/DrugDiscovery/PySCFIntegration.fsx](../examples/DrugDiscovery/PySCFIntegration.fsx) — complete integral-provider implementation
- [examples/Algorithms/AdaptVqe.fsx](../examples/Algorithms/AdaptVqe.fsx) — hand-built `PauliHamiltonian` into ADAPT-VQE
- [Error Mitigation](error-mitigation) — improving results on noisy backends
- [Backend Switching](backend-switching) — running the same Hamiltonian locally vs on Azure Quantum hardware
