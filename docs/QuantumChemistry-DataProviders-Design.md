# Quantum Chemistry – Pluggable Molecular & Atomic Data Design

## Motivation

Today, **QuantumChemistry** (and adjacent “example domains” like **DrugDiscovery** and **MaterialScience**) are limited by **data-as-code**:

- `src/FSharp.Azure.Quantum/Solvers/Quantum/QuantumChemistry.fs` contains element lists / convenience molecules.
- `src/FSharp.Azure.Quantum/Data/MolecularData.fs` contains small per-element tables (masses, TPSA/LogP contributions).
- `src/FSharp.Azure.Quantum/Data/MoleculeLibrary.fs` embeds a molecule library in source as CSV strings.

This is great for demos, but prevents real-world use where users need to:

- Import molecules from **XYZ, SDF, MOL2, PDB, SMILES, CSV** and custom formats.
- Plug in richer sources for element properties (isotopes, radii sets, etc.).
- Use external descriptor engines (e.g., RDKit) without forking core code.
- Load datasets from lab pipelines (files, databases, APIs) at scale.

Goal: make chemistry data **user-pluginnable** (similar spirit to `IntegralProvider`), while keeping:

- “Batteries included” defaults for onboarding.
- Separations between **domain model** vs **data acquisition**.
- Testability (providers are mockable).

Non-goal: implement a full cheminformatics stack inside the core library.

---

## Issues With The Previous Draft (Fixed Here)

The earlier version of this doc had several problems; this revision addresses them explicitly:

- **Not aligned with existing result/error conventions**: proposed `Result<_, string>` instead of `QuantumResult<_, QuantumError>`.
- **Conflated responsibilities**: “provider” mixed lookup, parsing, and geometry generation.
- **No geometry/conformer story**: real QC needs 3D coordinates; SMILES alone is insufficient.
- **No async/cancellation**: real imports/providers often need async I/O.
- **Too risky migration**: “one canonical Molecule type” could be a breaking refactor.

---

## Architecture Overview (Separation of Concerns)

Break chemistry “data” into 4 distinct layers:

1. **Element Properties** (symbol → atomic number/mass/radii, etc.)
2. **Molecular Topology** (SMILES/SDF → atoms/bonds graph; may be 2D)
3. **Geometry / Conformer** (topology → 3D coordinates)
4. **Datasets** (collections of molecules + labels + metadata)

Quantum chemistry solvers primarily consume:

- element properties
- molecules *with 3D geometry* (or a clear error when missing)

Drug discovery ML flows can consume:

- topology-only molecules (SMILES-based features)
- 3D geometry when available

---

## Proposed Abstractions (Aligned with `QuantumError` / `QuantumResult`)

### A) Element provider
Avoid hardcoding atomic numbers/masses in multiple places.

```fsharp
type ElementProperties = {
  AtomicNumber: int
  AtomicMass: float option
  CovalentRadius: float option
  Electronegativity: float option
}

type IElementProvider =
  abstract TryGetBySymbol: string -> ElementProperties option
  abstract TryGetByAtomicNumber: int -> ElementProperties option
```

Default:

- `PeriodicTable` already implements an extensible, CSV-driven dataset (`src/FSharp.Azure.Quantum/Data/PeriodicTable.fs`). We should build `PeriodicTableElementProvider` on top.

### B) Topology parsing (SMILES/SDF/etc.)
Parsing structured chemistry formats is not the same as *providing datasets*.

```fsharp
type MoleculeTopology = {
  Atoms: string array          // element symbols
  Bonds: (int * int * float option) array
  Charge: int option
  Multiplicity: int option
  Metadata: Map<string,string>
}

type IMoleculeTopologyParser =
  abstract SupportedFormats: string list // e.g. ["smiles"; "sdf"; ...]
  abstract Parse: input:string -> QuantumResult<MoleculeTopology>
```

This lets us keep `MolecularData`’s SMILES parser but move it conceptually into a “topology parser” layer.

### C) Geometry provider (the missing key)
This is the crucial piece for real QC usability.

```fsharp
type AtomGeometry = { X: float; Y: float; Z: float }

type MoleculeGeometry = {
  Atoms: AtomGeometry array
  Units: string // "angstrom" default
}

type IGeometryProvider =
  abstract TryGetGeometry: elementProvider:IElementProvider -> topology:MoleculeTopology -> QuantumResult<MoleculeGeometry option>
```

Examples:

- `XyzGeometryProvider` (geometry comes from XYZ file, no conformer gen)
- `PdbGeometryProvider`
- `RdkitGeometryProvider` (external python bridge, optional package)

Rule: QC solvers should require geometry, and fail with a clear `QuantumError.ValidationError` when absent.

### D) Dataset providers (collections)
A dataset provider returns `Molecule` instances plus labels, and is allowed to do I/O.

```fsharp
type MoleculeInstance = {
  Id: string option
  Name: string option
  Topology: MoleculeTopology
  Geometry: MoleculeGeometry option
}

type MoleculeDataset = {
  Molecules: MoleculeInstance array
  Labels: int array option
  LabelColumn: string option
}

type DatasetQuery =
  | ByName of string
  | ByPath of string

type IMoleculeDatasetProvider =
  abstract Describe: unit -> string
  abstract Load: query:DatasetQuery -> QuantumResult<MoleculeDataset>
```

This keeps “dataset concerns” (labels, file sets, metadata) separate from “parsing” and “geometry”.

---

## Async & Cancellation (Recommended)

Real providers will be async. The core library already uses async in multiple places.

Preferred shape:

- Provide both sync and async variants, or just async:

```fsharp
type IMoleculeDatasetProviderAsync =
  abstract LoadAsync: query:DatasetQuery -> Async<QuantumResult<MoleculeDataset>>
```

If the library wants to stay sync at core, then provide adapters (but this should be decided deliberately).

---

## Caching & Performance

### Practical performance assumption

- **Quantum chemistry (QC)**: when scoped correctly (small molecule / small active space / minimal basis), overall runtime is dominated by **compute** (integrals + Hamiltonian construction + VQE/QPE + classical optimizer), not by dataset retrieval.
- Therefore, the main reason for providers in QC is **extensibility and correctness**, not micro-optimizing I/O.

### Where data performance *can* matter

- **DrugDiscovery / screening**: you may load large SMILES/CSV datasets and then filter down to a small QC subset.
- **Geometry generation / descriptors**: conformer generation or descriptor engines (e.g., RDKit/OpenBabel/Python) can be compute-heavy even for small molecule counts.
- **Remote providers**: network-backed datasets (internal DB, PubChem) are latency-bound.

Given that, caching and async support should be:

- **Optional for local file import** (QC-friendly, keep simple).
- **Strongly recommended for external providers and screening/ML flows**.

### Caching boundaries

Providers should define where caching happens:

- **Element provider**: in-memory map (already done via `PeriodicTable`).
- **Topology parsing**: cache by input string hash (optional).
- **Geometry generation**: cache by (smiles hash + generator version + parameters).
- **Dataset**: cache by file hash / last-write timestamp.

A simple boundary:

- Provide a `CachingDatasetProvider(inner, cacheDir)` wrapper in core.
- Don’t bake caching into every provider.

---

## Builder / DSL Integration (No Global Registry by Default)

Instead of global registries, extend the `quantumChemistry {}` builder to accept provider configuration explicitly.

Example (conceptual):

```fsharp
let problem = quantumChemistry {
  elementProvider PeriodicTableElementProvider.instance
  datasetProvider (CsvDatasetProvider(...))
  geometryProvider (XyzGeometryProvider())
  moleculeId "H2O" // or query / selection
  basis "sto-3g"
  ansatz UCCSD
}
```

Key point: QC builder should “hydrate” a solver-ready molecule by:

1. load dataset/topology
2. ensure geometry (from dataset or geometry provider)
3. pass molecule to integrals/VQE

---

## What’s “Latest / Correct” In This Codebase Right Now

Based on the current sources, there are *two* parallel ways to get molecules:

- **Domain Builder convenience functions** in `QuantumChemistry.fs` under `module QuantumChemistryBuilder` (e.g. `h2`, `h2o`, `lih`).
- **`Molecule` module factory functions** earlier in the same file (e.g. `Molecule.createH2`, `Molecule.createH2O`, `Molecule.createLiH`, plus many materials-science helpers like `createFe2`, `createFeH`, etc.).
- Additionally, there’s **`Data.MoleculeLibrary`** which is a larger curated library, but still embedded as strings.

This is exactly why the project feels “complex”: there isn’t a single blessed entry point, and the same molecule (e.g. water) can be created in multiple places with slightly different APIs/conventions.

The design goal should not be “delete all helpers”; it should be:

- Keep a small number of *thin convenience wrappers* for onboarding.
- Move all substantial datasets and geometry definitions into **providers/importers**, not solver code.

---

## Inventory: Current Entry Points (QC + DrugDiscovery/MaterialScience)

### QuantumChemistry (QC solver side)

**1) `QuantumChemistry.Molecule.*` factory helpers (hardcoded geometry in solver file)**

Defined in `src/FSharp.Azure.Quantum/Solvers/Quantum/QuantumChemistry.fs` under `module Molecule`:

- `Molecule.createH2 (bondLength)`
- `Molecule.createH2O ()`
- `Molecule.createLiH (bondLength)`
- Materials-science oriented helpers:
  - `Molecule.createFe2 (bondLength) (multiplicity)`
  - `Molecule.createFeH (bondLength)`
  - `Molecule.createSiH4 ()`
  - `Molecule.createPH3 ()`
  - `Molecule.createCdSe (bondLength)`
  - (and likely more further down)

**2) `QuantumChemistryBuilder.*` convenience constructors (also hardcoded geometry)**

Defined in `src/FSharp.Azure.Quantum/Solvers/Quantum/QuantumChemistry.fs` under `module QuantumChemistryBuilder`:

- `h2 (distance)`
- `h2o (bondLength) (angle)`
- `lih (distance)`

**3) Data-layer curated molecule set exposed through conversion helpers**

Also in `QuantumChemistry.fs`:

- `Molecule.fromLibrary`
- `Molecule.tryFromLibrary`
- `Molecule.fromLibraryByName`

These already hint at the intended layering: **Data owns the dataset**, Solver just converts.

**4) QC file import currently lives in solver file**

In `QuantumChemistry.fs` under `module MolecularInput`:

- `fromXYZAsync` / obsolete `fromXYZ`
- `fromFCIDumpAsync` / obsolete `fromFCIDump`
- `toXYZ`, `saveXYZAsync`, etc.

These are “providers/importers” but are currently implemented inside the QC solver file, which increases complexity.

### DrugDiscovery (ML feature pipeline side)

Defined in `src/FSharp.Azure.Quantum/Business/QuantumDrugDiscovery.fs`:

- Loads molecule sets directly via:
  - `MolecularData.loadFromCsv path "SMILES" (Some "Label")`
  - `MolecularData.loadFromSmilesList lines`
- Derived features via:
  - `MolecularData.withDescriptors`
  - `MolecularData.withFingerprints`
  - `MolecularData.toFeatureMatrix`

So DrugDiscovery already uses a *file-based data acquisition approach*, but it is:

- hard-wired to a small set of file conventions
- not expressed as a plug-in provider chain

### MaterialScience

Material science currently shows up as hardcoded molecule constructors inside QC (`createFe2`, `createCdSe`, etc.) and as curated entries in `Data.MoleculeLibrary` categories.

---

## Cleanup / Deprecation Plan (Practical)

### 1) Decide the single “construction surface” per concern

- **For QC solvers**: the only truly correct input is a *solver-ready molecule with 3D geometry*.
- **For demos and docs**: keep 2–5 helpers (H2, LiH, H2O) but implement them by delegating to providers.
- **For curated datasets**: `MoleculeLibrary` should become a dataset provider (or be moved into a dataset package), not a module with embedded strings.

### 2) Introduce provider-backed convenience APIs (keep names, change internals)

Instead of duplicating molecule definitions across `Molecule.createH2O` and `QuantumChemistryBuilder.h2o`, keep the public names but route them through a single default dataset provider, e.g.:

- `QuantumChemistryBuilder.h2` → calls `GeometryPresets.h2 distance` (a tiny pure function), OR uses the default dataset provider when distance is omitted.
- `QuantumChemistryBuilder.h2o bond angle` → stays (because it’s parameterized), but is re-homed into a dedicated `GeometryPresets` module.
- `Molecule.createH2O()` (no parameters) should become a *deprecated alias* that calls the same canonical implementation.

This avoids breaking user code while shrinking the number of definitions.

### 3) Move “materials science molecules” out of `QuantumChemistry.fs`

The long list of `createFe2`, `createFeH`, `createSiH4`, etc. in solver code is effectively a dataset.

Plan:

- Keep them temporarily as thin wrappers.
- Add a `MaterialsDatasetProvider` (built-in, but data-driven).
- Implement wrappers by doing `provider.TryGet (ByName "Fe2")` etc.

### 4) Remove hardcoded element lists and mass tables first

This is the nicest early win because it reduces duplicated truth:

- Replace `AtomicNumbers` with `PeriodicTable`-backed lookup.
- Replace `MolecularData.atomicMasses` with `IElementProvider`.

### 5) Documentation changes to reflect the blessed path

Update docs so they don't teach multiple approaches.

- "Quick start" uses builder convenience (`h2 0.74`) but explicitly calls it a shortcut.
- "Real world" section shows `moleculeFromFile` (XYZ/PDB/CSV/SMILES) and provider configuration.

---

## Migration Plan (Safer, Non-breaking First)

### Phase 1: Introduce provider interfaces (additive) ✅ COMPLETE

**Status**: Implemented in `src/FSharp.Azure.Quantum/Data/ChemistryDataProviders.fs`

**What was added**:

1. **IElementProvider** - Wraps `PeriodicTable` with a clean interface:
   ```fsharp
   type IElementProvider =
     abstract TryGetBySymbol: symbol: string -> ElementProperties option
     abstract TryGetByAtomicNumber: atomicNumber: int -> ElementProperties option
     abstract IsValidSymbol: symbol: string -> bool
     abstract EstimateBondLength: symbol1: string -> symbol2: string -> float option
   ```

2. **IMoleculeTopologyParser** - For SMILES/SDF parsing (interface only, implementations TBD):
   ```fsharp
   type IMoleculeTopologyParser =
     abstract SupportedFormats: string list
     abstract Parse: input: string -> QuantumResult<MoleculeTopology>
     abstract ParseWithFormat: format: string -> input: string -> QuantumResult<MoleculeTopology>
   ```

3. **IGeometryProvider** - The critical missing piece for real QC:
   ```fsharp
   type IGeometryProvider =
     abstract TryGetGeometry: topology: MoleculeTopology -> QuantumResult<MoleculeGeometry option>
   
   type IGeometryProviderAsync =
     abstract TryGetGeometryAsync: topology: MoleculeTopology -> Async<QuantumResult<MoleculeGeometry option>>
   ```

4. **IMoleculeDatasetProvider** - Unified dataset access:
   ```fsharp
   type IMoleculeDatasetProvider =
     abstract Describe: unit -> string
     abstract Load: query: DatasetQuery -> QuantumResult<MoleculeDataset>
     abstract ListNames: unit -> string list
   
   type IMoleculeDatasetProviderAsync =
     abstract Describe: unit -> string
     abstract LoadAsync: query: DatasetQuery -> Async<QuantumResult<MoleculeDataset>>
     abstract ListNamesAsync: unit -> Async<string list>
   ```

5. **Built-in implementations**:
   - `PeriodicTableElementProvider` - Default element provider
   - `BuiltInMoleculeDatasetProvider` - Wraps `MoleculeLibrary`
   - `XyzFileDatasetProvider` - Loads molecules from XYZ files
   - `CachingDatasetProvider` - Caching wrapper for any provider
   - `NoGeometryProvider` - Passthrough for topology-only workflows

6. **Conversion utilities**:
   - `Conversions.toMoleculeEntry` - New types → legacy types
   - `Conversions.fromMoleculeEntry` - Legacy types → new types
   - `XyzImporter.toMoleculeInstance` - XYZ → unified instance

### Phase 2: Refactor internals to *use* providers ✅ COMPLETE

**What was done**:

1. Replaced hardcoded `atomicMasses` map in `MolecularData.fs` with `PeriodicTable` lookup:
   ```fsharp
   // Before:
   let private atomicMasses = Map.ofList [("H", 1.008); ("C", 12.011); ...]
   
   // After:
   let private getAtomicMass (symbol: string) : float option =
       PeriodicTable.tryGetBySymbol symbol
       |> Option.bind (fun e -> e.AtomicMass)
   ```

2. Replaced `AtomicNumbers.fromSymbol/toSymbol` in `QuantumChemistry.fs` with `PeriodicTable`:
   ```fsharp
   // Before:
   AtomicNumbers.fromSymbol symbol
   
   // After:
   PeriodicTable.getAtomicNumber symbol
   ```

3. Added provider-based molecule loading functions to `QuantumChemistry.Molecule`:
   ```fsharp
   /// Load molecule from a MoleculeInstance (provider result)
   let fromInstance (instance: MoleculeInstance) : Molecule option
   
   /// Load molecule from a dataset provider by name
   let fromProvider (provider: IMoleculeDatasetProvider) (name: string) : Molecule option
   
   /// Load from default built-in provider
   let fromDefaultProvider (name: string) : Molecule option
   ```

4. Added integration tests for provider-based molecule loading (24 tests in `MoleculeLibraryIntegrationTests.fs`)

### Phase 3: Add importers and dataset packages 🔄 IN PROGRESS

**File format support priority**:

| Format | Priority | Status | Notes |
|--------|----------|--------|-------|
| XYZ | v1 | ✅ Done | `XyzImporter` in ChemistryDataProviders |
| CSV+SMILES | v1 | ✅ Done | `CsvSmilesDatasetProvider` in SmilesDataProviders |
| SMILES List | v1 | ✅ Done | `SmilesListDatasetProvider` in SmilesDataProviders |
| Composite | v1 | ✅ Done | `CompositeDatasetProvider` - tries multiple providers |
| SDF/MOL | v1.5 | 📋 Planned | Interface ready, implementation TBD |
| PDB | v2 | 📋 Planned | Ligand extraction focus |
| FCIDump | v2 | 🔄 Partial | Existing in `MolecularInput`, needs provider alignment |

**SMILES-based providers** (NEW - implemented in `SmilesDataProviders.fs`):

1. `CsvSmilesDatasetProvider` - Loads molecules from CSV files with SMILES column:
   ```fsharp
   let provider = CsvSmilesDatasetProvider("molecules.csv", "SMILES", Some "Activity")
   match provider.Load All with
   | Ok dataset -> printfn "Loaded %d molecules" dataset.Molecules.Length
   | Error e -> printfn "Error: %A" e
   ```

2. `SmilesListDatasetProvider` - In-memory SMILES list provider:
   ```fsharp
   let smiles = ["CCO"; "CC(=O)O"; "c1ccccc1"]  // Ethanol, acetic acid, benzene
   let provider = SmilesListDatasetProvider(smiles)
   ```

3. `CompositeDatasetProvider` - Tries multiple providers in sequence:
   ```fsharp
   // Try built-in library first, then fall back to CSV file
   let provider = CompositeDatasetProvider [
       defaultDatasetProvider
       CsvSmilesDatasetProvider("custom_molecules.csv", "SMILES")
   ]
   ```

4. Convenience functions:
   - `parseSmiles` - Parse single SMILES to `MoleculeInstance`
   - `parseSmilesMany` - Parse multiple SMILES, returning successful parses
   - `fromSingleSmiles` - Create provider from single SMILES string

**Note**: SMILES molecules are **topology-only** (no 3D geometry). For quantum chemistry
calculations requiring geometry, combine with a geometry provider or use XYZ files.

**Dataset packaging options**:

- **Option A (recommended)**: Embedded CSV resources (like `PeriodicTable`)
  - Keeps library self-contained
  - Easy versioning
  - No runtime dependencies

- **Option B**: Separate NuGet package
  - Smaller core library
  - Independent dataset updates
  - More flexible for large datasets

### Phase 4: Optional external providers 📋 PLANNED

External providers live in separate packages to avoid dependency bloat:

1. **FSharp.Azure.Quantum.Chemistry.RDKit** (Python bridge):
   - `RdkitGeometryProvider` - Conformer generation
   - `RdkitTopologyParser` - SMILES/SDF parsing
   - `RdkitDescriptorProvider` - Molecular descriptors

2. **FSharp.Azure.Quantum.Chemistry.PubChem** (HTTP API):
   - `PubChemDatasetProvider` - Query by CID/name
   - `PubChemPropertyProvider` - Fetch computed properties

---

## Concrete Deprecation Path for Hardcoded Molecules

### Step 1: Keep existing API, change implementation

```fsharp
// Before (in QuantumChemistry.fs):
module Molecule =
    let createH2 bondLength =
        { Formula = "H2"
          Atoms = [| {Symbol="H"; Position=(0.0, 0.0, 0.0)}
                     {Symbol="H"; Position=(bondLength, 0.0, 0.0)} |]
          // ... hardcoded geometry
        }

// After:
module Molecule =
    let private defaultProvider = ChemistryDataProviders.defaultDatasetProvider
    
    /// Creates H2 molecule with specified bond length.
    /// NOTE: For production use, prefer loading from providers or XYZ files.
    let createH2 bondLength =
        // Still works the same externally, but internally could be:
        // 1. Keep hardcoded for now (no breaking change)
        // 2. Later: use GeometryPresets module
        { Formula = "H2"
          Atoms = [| {Symbol="H"; Position=(0.0, 0.0, 0.0)}
                     {Symbol="H"; Position=(bondLength, 0.0, 0.0)} |]
          // ...
        }
    
    /// NEW: Load molecule from the built-in library
    let fromLibrary name = 
        match defaultProvider.Load (DatasetQuery.ByName name) with
        | Ok ds -> Conversions.toQuantumMolecule ds.Molecules.[0]
        | Error e -> failwithf "Failed to load '%s': %A" name e
```

### Step 2: Add `[<Obsolete>]` to convenience constructors (later)

```fsharp
[<Obsolete("Use Molecule.fromLibrary or load from XYZ file. Will be removed in v3.0.")>]
let createH2O () = ...
```

### Step 3: Move geometry presets to dedicated module

```fsharp
/// Geometry presets for common molecules (parametric).
/// These are thin helpers for demos; production code should use providers.
module GeometryPresets =
    
    /// H2 with parameterized bond length (default: 0.74 Å)
    let h2 ?(bondLength: float = 0.74) =
        { Atoms = [| "H"; "H" |]
          Coordinates = [| {X=0.0; Y=0.0; Z=0.0}; {X=bondLength; Y=0.0; Z=0.0} |]
          Units = "angstrom" }
    
    /// H2O with parameterized bond length and angle
    let h2o ?(bondLength: float = 0.96) ?(angle: float = 104.5) =
        let angleRad = angle * Math.PI / 180.0
        { Atoms = [| "O"; "H"; "H" |]
          Coordinates = [| 
            {X=0.0; Y=0.0; Z=0.0}
            {X=bondLength; Y=0.0; Z=0.0}
            {X=bondLength * cos(angleRad); Y=bondLength * sin(angleRad); Z=0.0} |]
          Units = "angstrom" }
```

---

## Usage Examples

### Example 1: Load from built-in library (simplest)

```fsharp
open FSharp.Azure.Quantum.Data.ChemistryDataProviders

let provider = defaultDatasetProvider

// Load single molecule
match provider.Load (ByName "water") with
| Ok dataset -> 
    let molecule = dataset.Molecules.[0]
    printfn "Loaded: %s with %d atoms" 
        (molecule.Name |> Option.defaultValue "unknown")
        molecule.Topology.Atoms.Length
| Error e -> printfn "Error: %A" e

// Load all diatomic molecules
match provider.Load (ByCategory "diatomic") with
| Ok dataset ->
    for mol in dataset.Molecules do
        printfn "- %s" (mol.Name |> Option.defaultValue "?")
| Error e -> printfn "Error: %A" e
```

### Example 2: Load from XYZ file

```fsharp
open FSharp.Azure.Quantum.Data.ChemistryDataProviders

// Async loading with charge/multiplicity
let loadMolecule() = async {
    let! result = XyzImporter.loadAsMoleculeInstanceAsync 
                    "molecules/caffeine.xyz" 
                    (Some 0)      // neutral
                    (Some 1)      // singlet
    
    match result with
    | Ok molecule ->
        match molecule.Geometry with
        | Some geom -> 
            printfn "Loaded %d atoms with 3D geometry" geom.Coordinates.Length
        | None -> 
            printfn "Topology only (no geometry)"
    | Error e ->
        printfn "Failed: %A" e
}
```

### Example 3: Custom directory provider

```fsharp
// Load all XYZ files from a directory
let provider = XyzFileDatasetProvider("/path/to/molecules")

// Async enumeration
let listMolecules() = async {
    let! names = (provider :> IMoleculeDatasetProviderAsync).ListNamesAsync()
    for name in names do
        printfn "Available: %s" name
}
```

### Example 4: Using element provider for atomic data

```fsharp
let elements = defaultElementProvider

// Get carbon properties
match elements.TryGetBySymbol "C" with
| Some c ->
    printfn "Carbon: Z=%d, mass=%.3f" c.AtomicNumber c.AtomicMass
    match c.CovalentRadius with
    | Some r -> printfn "Covalent radius: %.2f Å" r
    | None -> printfn "No radius data"
| None ->
    printfn "Unknown element"

// Estimate C-H bond length
match elements.EstimateBondLength "C" "H" with
| Some len -> printfn "C-H bond: ~%.2f Å" len
| None -> printfn "Cannot estimate"
```

---

## What This Enables

- QuantumChemistry becomes usable with:
  - XYZ geometries from real QC pipelines
  - PDB-derived geometries (after filtering)
  - user-specific datasets without editing source

- DrugDiscovery becomes extensible with:
  - custom CSV formats
  - external descriptor calculators
  - consistent dataset abstraction

---

## Open Decisions

1. **Async-first?**
   - Strongly recommended for providers.
   - ✅ Implemented: Both sync and async interfaces provided.

2. **What is the "solver-ready molecule" type?**
   - Keep existing `QuantumChemistry.Molecule` public API for now.
   - Add conversions from `MoleculeInstance` → `QuantumChemistry.Molecule` internally.
   - ✅ Implemented: `Conversions` module provides bidirectional mapping.

3. **How strict should geometry requirement be?**
   - For QC solvers: strict "must have geometry".
   - For ML pipelines: allow topology-only.
   - ✅ Implemented: `MoleculeInstance.Geometry` is `option` type.

---

## Related Research

- Dataset/format research note: `docs/Chemistry-Datasets-Research-Note.md`

## Summary

The key fix is to add a missing middle layer: **geometry/conformer provisioning**, and to separate parsing, geometry, and datasets. Combined with alignment to `QuantumError`/`QuantumResult` and an additive migration path, this makes the chemistry stack extensible without a massive breaking refactor.

**Implementation status**:
- ✅ Phase 1: Provider interfaces implemented
- ✅ Phase 2: Internal refactoring complete (PeriodicTable integration, provider-based loading)
- 🔄 Phase 3: In progress (SMILES providers done, SDF/PDB planned)
- 📋 Phase 4: Planned for future iterations (external RDKit/PubChem providers)
