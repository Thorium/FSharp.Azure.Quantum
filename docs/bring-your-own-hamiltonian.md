---
layout: default
title: Bring Your Own Hamiltonian
---

# Bring Your Own Hamiltonian

**Plug external quantum chemistry packages into the library** — the built-in chemistry stack computes STO-3G integrals itself only for molecules made of hydrogen and helium atoms; for everything else the Hamiltonian builders and the VQE layers accept externally computed data. This page maps out where to plug in, depending on what your external tool produces.

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
| Molecular integrals from PySCF / Psi4 / NWChem | Integral level | An `IntegralProvider` in `SolverConfig.IntegralProvider` (VQE via `GroundStateEnergy.estimateEnergyAsync`), or `MolecularIntegrals` → `MolecularHamiltonian.buildFromIntegrals` |
| An FCIDUMP file (standard interchange format) | Integral level | `FciDumpIntegrals.fromFile` (an `IntegralProvider`) or `FciDumpIntegrals.readFile` (see below) |
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
            NuclearRepulsion = 0.713754
            OneElectron = { NumOrbitals = 2; Integrals = reference.OneElectron.Integrals }  // float[,]
            TwoElectron = { NumOrbitals = 2; Integrals = reference.TwoElectron.Integrals }  // float[,,,]
            ReferenceEnergy = Some -1.116707                                                // Hartree-Fock
        }

let molecule = Molecule.createH2 0.7414

match MolecularHamiltonian.buildWithMapping molecule MolecularHamiltonian.JordanWigner (Some myProvider) with
| Ok hamiltonian -> printfn "%d qubits, %d Pauli terms" hamiltonian.NumQubits hamiltonian.Terms.Length
| Error err -> eprintfn "Hamiltonian failed: %s" err.Message
```

If you already hold the integrals, skip the provider and call `MolecularHamiltonian.buildFromIntegrals`. It returns the qubit Hamiltonian together with the nuclear repulsion energy, which you add to the VQE result. `FermionMapping.ChemistryVQE.runAsync` runs a UCCSD VQE on that Hamiltonian:

```fsharp
open System.Threading
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

    let result =
        ChemistryVQE.runAsync vqeConfig CancellationToken.None
        |> Async.AwaitTask
        |> Async.RunSynchronously

    match result with
    | Ok result -> printfn "Total energy: %.6f Ha" (result.Energy + nuclearRepulsion)  // ≈ -1.137 Ha
    | Error err -> eprintfn "VQE failed: %s" err.Message
```

**`SolverConfig.IntegralProvider`:** `GroundStateEnergy.estimateEnergyAsync` with `Method = GroundStateMethod.VQE` (or `Automatic`) does the steps above for you. It calls the provider, builds the Jordan-Wigner Hamiltonian from the integrals and runs UCCSD-VQE on the configured backend, adding the nuclear repulsion to the energy. A provider `Error` comes back as an `Error`. Without a provider:

- molecules made only of H and He atoms, at any geometry, use integrals the library computes itself. `Sto3gIntegrals.computeInBasis` supports STO-3G, the default, and 6-31G (`VQE.runInBasisAsync`, or `basis` in the `quantumChemistry` builder). It uses the lowest RHF solution it finds (several starting guesses, DIIS and damped Roothaan iterations, orbital-Hessian stability check), or core-Hamiltonian orbitals for a single electron. If no SCF converges it also uses core-Hamiltonian orbitals, sets `ReferenceEnergy = None` and says so in `VQEResult.Notes`;
- H₂O and LiH return an `Error` asking for an `IntegralProvider`;
- other molecules run a hardware-efficient VQE on the empirical prototype Hamiltonian, whose energies are not physical.

The result's `Source` field (`EnergySource`) records which of these produced the energy: `ProviderIntegrals`, `ComputedSto3gIntegrals`, `Computed631gIntegrals`, `EmpiricalHamiltonian`, or `TabulatedReference`. `TabulatedReference` is the fixed reference value of `GroundStateMethod.ClassicalDFT`, returned only when that method is asked for.

`Estimation` records how the energy was measured. `ExactExpectation` means state-vector amplitudes on a simulator that applies gates one at a time. `SampledCircuits(circuitsPerEnergy, shotsPerCircuit, circuitsExecuted)` means a backend that runs only whole circuits (cloud hardware, `NoisyLocalBackend`). There the UCCSD circuit, one measurement circuit per qubit-wise commuting group of terms, and an SPSA optimiser replace the gate-by-gate BFGS. `Converged` there means no improvement above the shot noise for 30 iterations. A run over the circuit budget is refused up front. See [Hardware Selection](Hardware-Selection-Guide.md) for the budget and measured accuracy. `FermionMapping.ChemistryVQE.uccsdCircuit`, `measurementGroups`, `measurementCircuit` and `sampledExpectation` expose the same steps for your own use. The circuit applies U = e^(T − T†) by Trotterised Pauli rotations, so amplitudes follow the usual coupled-cluster sign convention. MP2 or CCSD amplitudes from a chemistry package can seed `InitialParameters` once they are put in the excitation pool's order (singles, then doubles; `UCCSD.generateExcitationPool`).

Some requests are an `Error` rather than a silent approximation:

- The UCCSD ansatz starts from a Hartree-Fock reference and does not conserve spin, so it supports closed shells (even electron count, multiplicity 1) and one-electron doublets only. Other multiplicities, and FCIDUMP files with `MS2` above the lowest value for their `NELEC`, are refused.
- UCCSD with more than `VQE.MaxUccsdParameters` (64) parameters is refused before it runs. That covers 4 electrons in 4 orbitals, but not an H6 chain in STO-3G (261 parameters); choose a smaller active space in your chemistry package.
- On backends that sample measurements, `SolverConfig.ErrorMitigation` corrects each histogram with the inverse readout calibration, without clipping or filtering. A strategy that cannot be applied to a histogram (ZNE or PEC, alone or combined) or that corrects nothing is an `Error`. `ErrorMitigationApplied` on the result says whether a correction ran; it is false when `Estimation = ExactExpectation`, since exact expectations have no readout to correct.

```fsharp
open System.Threading

let config =
    { Method = GroundStateMethod.VQE
      MaxIterations = 100
      Tolerance = 1e-6
      InitialParameters = None
      Backend = None                       // LocalBackend
      ProgressReporter = None
      ErrorMitigation = None
      IntegralProvider = Some myProvider }

task {
    match! GroundStateEnergy.estimateEnergyAsync molecule config CancellationToken.None with
    | Ok result -> printfn "%.6f Ha from %A after %d iterations" result.Energy result.Source result.Iterations
    | Error err -> eprintfn "VQE failed: %s" err.Message
}
```

The `quantumChemistry` computation expression takes a provider through its `integralProvider` operation.

**Requirements for the integrals** (see the full preconditions block in `Solvers/Quantum/QuantumChemistry.fs`):

- Molecular-orbital (MO) basis, not atomic-orbital — transform first (`h_MO = Cᵀ h_AO C`, use `ao2mo` in PySCF).
- Two-electron integrals in **chemist notation** `(pq|rs)`. PySCF uses this by default; Psi4 returns physicist notation `<pr|qs>` and needs conversion.
- Energies in Hartree; `Molecule` positions are in Angstroms.
- ≤ 10 spatial orbitals (the spin-orbital expansion doubles this to 20 qubits). `buildFromIntegrals` returns an `Error` for anything larger, whatever the backend. Larger molecules need an active-space selection in your external package before handing over the integrals.

**Working example**: [examples/DrugDiscovery/PySCFIntegration.fsx](../examples/DrugDiscovery/PySCFIntegration.fsx) implements a PySCF-backed provider via pythonnet, including basis-set selection and validation against the Hartree-Fock reference energy. [examples/Chemistry/H2_UCCSD_VQE_Example.fsx](../examples/Chemistry/H2_UCCSD_VQE_Example.fsx) runs the UCCSD VQE shown above.

## 2. FCIDUMP files

Most quantum chemistry packages (PySCF's `pyscf.tools.fcidump`, Psi4, Molpro, OpenMolcas, Q-Chem, ...) export FCIDUMP. `FciDumpIntegrals` reads the whole file, header and integrals, into `MolecularIntegrals`:

```fsharp
// An IntegralProvider for VQE: the file's integrals, whatever molecule it is called with
let config = { config with IntegralProvider = Some(FciDumpIntegrals.fromFile "h2o-cas.fcidump") }

// Or the integrals themselves
match FciDumpIntegrals.readFile "h2o-cas.fcidump" with
| Ok integrals -> printfn "%d orbitals, %d electrons, core energy %.6f Ha" integrals.NumOrbitals integrals.NumElectrons integrals.NuclearRepulsion
| Error err -> eprintfn "%s" err.Message
```

The reader takes the standard restricted-orbital format: an `&FCI NORB=…, NELEC=…, MS2=…, ORBSYM=…, ISYM=…` header ended by `&END` or `/`, then `value i j k l` lines with 1-based orbital indices in chemists' notation. `i j k l > 0` is the two-electron integral `(ij|kl)`, and all eight permutationally equivalent entries are filled. `k = l = 0` is the one-electron integral `h_ij`, and `i = j = k = l = 0` is the core energy (nuclear repulsion plus any frozen-core energy), which becomes `NuclearRepulsion`. Fortran `D` exponents are accepted. A malformed line, an index above `NORB`, a missing header end or an unrestricted file (`IUHF=1`) is an `Error` naming the problem. `MoleculeFormats.FciDump.parseIntegrals` gives the same data before conversion.

FCIDUMP files carry no geometry. The integrals must belong to the molecule you pass alongside them, and an active space chosen in the external package keeps the qubit count down (2 qubits per active orbital). `quantumChemistry { molecule_from_fcidump path; ... }` runs VQE on the file's integrals. `Molecule.fromFciDumpFileAsync` and the `FciDump*DatasetProvider` types still read only the header, for metadata.

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

`qubitH` is a `QubitHamiltonian`, the input `ChemistryVQE.runAsync` takes (section 1); `toQaoaHamiltonian` converts it to the `ProblemHamiltonian` form used by the QAOA and Hamiltonian-builder code.

`HamiltonianSimulation` evolves a state under a `ProblemHamiltonian` by Trotter-Suzuki decomposition. `simulateFromPreparation` starts from a gate circuit that prepares the initial state, so it runs on every backend: gate by gate on a simulator (`Route = GateByGate`, exact probabilities and the final state), and on a cloud backend as one whole circuit, preparation plus Trotter gates (`Route = WholeCircuit shots`, measured probabilities only):

```fsharp
open FSharp.Azure.Quantum

// Occupy spin orbitals 0 and 1, then evolve under problemH for t = 1.0
let preparation =
    CircuitBuilder.empty problemH.NumQubits
    |> CircuitBuilder.addGate (CircuitBuilder.X 0)
    |> CircuitBuilder.addGate (CircuitBuilder.X 1)

let evolutionConfig : HamiltonianSimulation.SimulationConfig =
    { Time = 1.0; TrotterSteps = 10; TrotterOrder = 2; Backend = None }   // None = local simulator

match HamiltonianSimulation.simulateFromPreparation problemH preparation evolutionConfig with
| Ok evolved -> printfn "%A: P(orbitals 0 and 1 occupied) = %.3f" evolved.Route evolved.Probabilities.[3]
| Error err -> printfn "%s" err.Message
```

Probabilities are indexed with qubit q as bit q of the index. `HamiltonianSimulation.simulate` takes an arbitrary `QuantumState` instead and returns it evolved, phases included; a cloud job cannot load or return such a state, so on a cloud backend `simulate` is an `Error` that names `simulateFromPreparation`.

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
- **Phase estimation**: `QPE.evolutionPlan` and `QPE.circuit` (in `QuantumChemistry`) build the controlled-e^(−iHt) phase-estimation circuit for a `PauliHamiltonian`; see [Quantum phase estimation of a molecular energy](#quantum-phase-estimation-of-a-molecular-energy) below.

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

### Quantum phase estimation of a molecular energy

`GroundStateMethod.QPE` (`QPE.runAsync`, or `QPE.runWithAsync settings`) estimates an energy by quantum phase estimation. It uses the same integral sources as VQE: an `IntegralProvider` or FCIDUMP file, else the library's STO-3G or 6-31G integrals for H and He. It needs no ansatz. The design:

- **Unitary.** U = e^(−i(H − shift)t) for the Jordan-Wigner Hamiltonian H = Σ c_k P_k. Every eigenvalue lies within λ = Σ|c_k| (non-identity terms) of the identity coefficient c_I. With shift = c_I + λ and t = 2π(1 − 2/2^m)/(2λ), each eigenvalue has its own phase φ ∈ [0, 1), and E = −2πφ/t + shift, plus nuclear repulsion.
- **Controlled powers.** Controlled-U^(2^j) repeats the same Trotter-Suzuki circuit for U 2^j times (`TrotterSuzuki.synthesizeControlledHamiltonianEvolution`; first order and 4 steps by default). QPE therefore measures the eigenvalues of the Trotterised U, and the Trotter error does not grow with j.
- **Readout.** The counting register gets `Algorithms.QPE.inverseQftGates`. The whole circuit is one job (`UnifiedBackend.submitAsCircuit`), so the local simulator and cloud backends run the same circuit. On a simulator the outcome probabilities are exact; a sampling backend returns frequencies (`ShotsPerCircuit`).
- **Energy.** Each peak of the outcome distribution is an eigenvalue. Its phase is refined between the peak's two highest bins with the exact QPE line shape, which recovers phases between bins. `Energy` is the most probable peak.
- **Result.** `Source = QpeTrotterEvolution`. `Estimation = PhaseEstimation` records t, the shift, the Trotter order and steps, the counting qubits, the bin width and every peak with its probability.

QPE returns eigenvalue E_k with probability |⟨ψ|E_k⟩|² for the prepared state ψ. By default ψ is the Hartree-Fock determinant; `UccsdState amplitudes` (for example a VQE result's `OptimalParameters`) is another choice. The reported energy is therefore the ground state only when ψ overlaps it most. `Notes` states the overlap and lists the other peaks.
```fsharp
open System.Threading
open FSharp.Azure.Quantum.QuantumChemistry

let qpeConfig =
    { Method = GroundStateMethod.QPE
      MaxIterations = 100
      Tolerance = 1e-6
      InitialParameters = None
      Backend = None                  // local simulator; a cloud backend runs the same circuit
      ProgressReporter = None
      ErrorMitigation = None
      IntegralProvider = None }       // H2: the library's STO-3G integrals

let stretched =
    QPE.runWithAsync { QPE.defaultSettings with CountingQubits = Some 6 } (Molecule.createH2 2.0) qpeConfig CancellationToken.None
    |> Async.AwaitTask
    |> Async.RunSynchronously

match stretched with
| Ok r ->
    match r.Estimation with
    | PhaseEstimation d -> d.Peaks |> List.iter (fun p -> printfn "E = %.6f Ha, probability %.2f" p.Energy p.Probability)
    | _ -> ()
    r.Notes |> List.iter (printfn "%s")
| Error e -> printfn "%s" e.Message
```

For H₂/STO-3G the defaults use 4 system and 8 counting qubits:

| Bond | Peaks (energy, probability) | vs FCI | Runtime |
|---|---|---|---|
| 0.7414 Å | −1.136546 Ha (0.98); 0.4791 Ha (0.01) | +0.72 mHa | about 7 s |
| 2.0 Å, 6 counting qubits | −0.948354 Ha (0.70); −0.3767 Ha (0.27, a doubly excited singlet) | +0.29 mHa | under 1 s |

What limits the accuracy:

- **Trotter error.** First order, 4 steps: 0.7 mHa at equilibrium. 8 steps give 0.3 mHa. Second order needs twice the gates per step for a similar error (1.3 mHa at 3 steps).
- **Phase resolution.** The bin width is 2π/(t·2^m): 14.8 mHa with 8 counting qubits. The refinement recovers the phase within a bin when one eigenvalue dominates the peak.

Both together stay within chemical accuracy (1.6 mHa) for H₂. The circuit holds (2^m − 1) × (gates per U) gates, about 146,000 here. Each extra counting qubit doubles it. A cloud backend accepts it as one job, but a circuit this deep is far beyond what today's hardware runs coherently, so hardware runs of chemistry QPE are impractical for now; UCCSD-VQE is the route for current devices.

## 5. Molecule data and geometry providers

To source molecular *structures* (rather than Hamiltonians) from external systems, implement the provider interfaces in `Data/ChemistryDataProviders.fs`:

- `IMoleculeDatasetProvider` / `IMoleculeDatasetProviderAsync` — molecule databases (query by name, list, describe)
- `IGeometryProvider` / `IGeometryProviderAsync` — 3D geometry generation or lookup
- `IElementProvider` — element metadata (defaults to the built-in periodic table)
- File parsers for XYZ, MOL2, PDB and SMILES are built in (`MoleculeFormats`, `SmilesDataProviders`)

<!-- fragment -->
```fsharp
let mol = Molecule.fromProvider myDatasetProvider "caffeine"                   // your IMoleculeDatasetProvider
let mol' = Molecule.fromXyzFileAsync "conformer42.xyz" CancellationToken.None  // your files
```

## Scale honestly

The built-in VQE path is validated against exact STO-3G energies for H₂ (the result's `Source` says which Hamiltonian ran), and the chemistry Hamiltonian builders refuse anything wider than 20 qubits (`Types.NisqPracticalQubits`). That is a fixed limit on every backend, not the local simulator's memory limit. "Bring your own Hamiltonian" does not remove that ceiling — it removes the *accuracy* ceiling (empirical vs research-grade integrals) and lets your external package do what it is good at (integrals, active-space selection, orbital localization) while this library does what it is good at (typed circuit construction, backend routing, error mitigation, Azure Quantum execution). For molecules beyond the qubit budget, reduce to an active space externally before handing over. A Pauli Hamiltonian passed straight to `AdaptVqe.run` or `Primitives.observe` is limited by the backend instead.

## See also

- [examples/DrugDiscovery/PySCFIntegration.fsx](../examples/DrugDiscovery/PySCFIntegration.fsx) — complete integral-provider implementation
- [examples/Chemistry/H2_UCCSD_VQE_Example.fsx](../examples/Chemistry/H2_UCCSD_VQE_Example.fsx) — UCCSD VQE on the H₂ integrals
- [examples/Algorithms/AdaptVqe.fsx](../examples/Algorithms/AdaptVqe.fsx) — hand-built `PauliHamiltonian` into ADAPT-VQE
- [Error Mitigation](error-mitigation) — improving results on noisy backends
- [Backend Switching](backend-switching) — running the same Hamiltonian locally vs on Azure Quantum hardware
