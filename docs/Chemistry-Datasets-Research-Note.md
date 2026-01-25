# Research Note: Chemistry Datasets & Standard Formats (QC + DrugDiscovery + Materials)

Purpose: identify **standard formats** and **credible open datasets / sources** to guide a practical “v1 support matrix” for pluggable chemistry data in this repo.

This note is scoped to *data acquisition* and dataset interchange, not to implementing full cheminformatics.

---

## 1) Standard Formats Worth Supporting

### A. Quantum Chemistry (QC) – geometry-centric

**1) XYZ (geometry)**
- What it represents: a simple list of atoms with 3D coordinates.
- Why it’s v1-worthy: ubiquitous in QC workflows, extremely simple.
- Limitations: no bonding, weak/optional charge/multiplicity metadata.

**2) PDB (geometry; proteins + ligands)**
- What it represents: biomolecular 3D structures.
- Why it’s relevant: DrugDiscovery workflows often start from PDB protein structures.
- Limitations: messy in practice (alt locations, waters, chain IDs, residues). Ligands may need extraction/cleanup.

**3) SDF (Structure Data File) (topology + optional 2D/3D coordinates)**
- What it represents: molecule records with atoms/bonds and (often) coordinates; used heavily in cheminformatics.
- Why it’s important: common export format for PubChem/ChEMBL and many pipelines.
- Limitations: parsing is more complex than XYZ/SMILES; stereochemistry and aromaticity details can matter.

**4) MOL2 (topology + partial charges + atom types)**
- Why it’s relevant: many docking/force-field workflows use MOL2.
- Limitations: parsing complexity; multiple dialects.

**5) FCIDump (integrals interchange)**
- What it represents: electronic structure integrals for FC/CI style workflows.
- Why it’s relevant: bridges from classical QC software (PySCF, etc.) to quantum algorithms.
- Caveat: often the most important part is the integrals, not geometry; robust parsing is non-trivial.

**V1 recommendation (QC):** XYZ first; keep FCIDump as “advanced” and focus on integrals-provider alignment.

---

### B. Drug Discovery (screening / ML) – large datasets + filtering

**1) SMILES-per-line (.smi / .txt)**
- What it represents: a list of SMILES strings, optionally with an ID column.
- Why it’s v1-worthy: universal lowest-common-denominator.
- Limitations: no geometry; conformer generation needed for QC.

**2) CSV/TSV**
- What it represents: arbitrary tabular datasets with columns like `SMILES`, `Name`, `Label`, properties.
- Why it’s v1-worthy: pipelines, labels, metadata, and filtering live here.
- Limitations: requires column mapping + missing-value strategy.

**3) SDF**
- Why: many public datasets distribute as SDF; can contain properties + coordinates.

**V1 recommendation (DrugDiscovery):** CSV + SMILES list first (already partially supported); then SDF.

---

### C. Materials Science

Materials often are not “molecules”; they are periodic crystal structures.

**1) CIF (Crystallographic Information File)**
- Standard for crystal structures.
- Used by Materials Project and many materials DBs.

**2) POSCAR/CONTCAR (VASP)**
- Widely used in computational materials science.

**Pragmatic v1 note:** If the library’s “QuantumChemistry” solver is only for *molecular* Hamiltonians right now, materials support should initially be framed as:

- small clusters / dimers / catalyst motifs (XYZ/SDF)
- quantum-dot fragments (XYZ)

**V1 recommendation (Materials):** treat materials as molecular fragments in XYZ first; keep CIF/POSCAR as v2+.

---

## 2) Credible Open Datasets / Sources

### A. Molecule registries (DrugDiscovery / general chemistry)

**PubChem**
- One of the largest open chemical databases.
- Provides identifiers, SMILES, SDF downloads, properties.
- Practical use: query by CID/name; download SDF/SMILES; fetch properties.

**ChEMBL**
- Bioactive molecules with assay data.
- Strong for DrugDiscovery workflows.

**ZINC**
- Large purchasable compound library used for virtual screening.
- Often exported as SMILES/SDF.

### B. QC-focused datasets (geometries + computed properties)

**QM9**
- Small organic molecules (up to 9 heavy atoms) with DFT-computed properties.
- Very common benchmark dataset.
- Useful for: small-data QC/ML; many geometries.

**ANI-1 / ANI-1x / ANI-1ccx**
- Large dataset of molecules with energies/forces (for ML potentials).
- Useful if a workflow wants many geometries.

**Open Quantum Materials Database (OQMD) / related**
- More materials-oriented than molecular QC.

### C. Materials databases

**Materials Project**
- Open materials structures and properties.
- Usually distributed with CIF-like structures.

**aflow / OQMD**
- Alternative materials repositories.

---

## 3) V1 Support Matrix (Recommended)

### V1 goal: practical, low-risk, high-value

**QC**
- Import: `XYZ` (first-class).
- Dataset: small curated library as provider (replacing embedded strings).
- Optional: minimal `PDB` ligand extraction (maybe v1.5).
- Not v1: full CIF/POSCAR; full SDF stereo/aromaticity correctness; full FCIDump integrals parsing.

**DrugDiscovery**
- Import: `CSV` (configurable columns), `SMILES list`.
- Data model: support labels + metadata.
- Optional: SDF import (v1.5).

**Materials (as molecular fragments)**
- Import: `XYZ`.
- Dataset: curated motifs in a dataset provider.
- Not v1: periodic crystals (CIF/POSCAR) unless there’s already a solver path for periodic systems.

---

## 4) Implementation Order (Suggested)

1. **Unify “data-as-code” behind dataset providers**
   - Convert `MoleculeLibrary` into a dataset provider (still embedded resource ok for v1).
   - Deprecate direct hardcoded molecule constructors by making them wrappers.

2. **Formalize “drug discovery dataset” provider**
   - Extract the CSV / SMILES list loading logic currently inside `QuantumDrugDiscovery.fs` into provider(s).

3. **Add XYZ importer/provider as first-class**
   - Move `MolecularInput.fromXYZAsync` out of solver file and into Data provider layer.

4. **Add extension packages (optional)**
   - PubChem/ChEMBL provider packages (HTTP) if needed.
   - RDKit/python provider for conformer generation and descriptors.

---

## 5) Risks / Gotchas

- **SMILES → geometry is not free**: conformer generation is compute-heavy and needs an external engine (RDKit/OpenBabel).
- **SDF/PDB correctness**: robust parsing is non-trivial; better to treat them as optional provider packages initially.
- **Materials ≠ molecules**: periodic systems need different abstractions (unit cell, symmetry, k-points). Don’t over-promise in v1.

---

## Summary

For v1, the best “standard format + open dataset” strategy is:

- QC: XYZ + curated small datasets.
- DrugDiscovery: CSV + SMILES list, with optional SDF later.
- Materials: treat as cluster fragments (XYZ) unless/until periodic solvers exist.

External internet sources (PubChem/ChEMBL/Materials Project) should be supported via **optional provider packages**, not baked into the core.
