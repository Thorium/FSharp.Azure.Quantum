# Pharmaceutical Glossary

Quick reference for domain terms used in these examples.
*Source: "A Practical Guide to Drug Development in Academia" (Mochly-Rosen & Grimes)*

## Target Product Profile (TPP)

Go/no-go criteria for drug candidates (from SPARK program):

| Parameter | Lead | Candidate | Drug-like |
|-----------|------|-----------|-----------|
| **Kd/IC50** | <10 μM | <100 nM | <10 nM |
| **Selectivity** | >10x | >100x | >1000x |
| **Bioavailability** | — | >20% | >50% |
| **Half-life** | — | supports dosing | 1x-2x daily |

## Drug Properties

| Term | Definition |
|------|------------|
| **Kd** | Dissociation constant - lower = tighter binding (nM-pM range is potent) |
| **IC50/EC50** | Concentration for 50% inhibition/effect |
| **Lipinski's Rule of 5** | Drug-likeness filter: MW<500, LogP<5, HBD<5, HBA<10 |
| **Veber's Rules** | Oral bioavailability: RotBonds≤10, PSA≤140 Å² |
| **ADMET** | Absorption, Distribution, Metabolism, Excretion, Toxicity |
| **TPSA** | Topological Polar Surface Area (Å²) - affects permeability |
| **LogP** | Partition coefficient (lipophilicity) - log([drug]octanol/[drug]water) |

## ADMET Detailed (see ADMETPrediction.fsx)

| Property | Target Range | Notes |
|----------|--------------|-------|
| **Bioavailability (F)** | >30% oral | AUC_oral / AUC_iv |
| **BBB Permeability** | MW<450, PSA<90, LogP 1-3 | CNS drugs need BBB+ |
| **hERG IC50** | >10 μM | <1 μM = high cardiac risk |
| **CYP Inhibition** | IC50 >10 μM | Avoid drug-drug interactions |
| **Metabolic t½** | >30 min (human microsomes) | Stability in liver |
| **Plasma Protein Binding** | <99% | High PPB reduces free drug |

## Pharmacokinetics (PK) Equations

| Equation | Formula | Use |
|----------|---------|-----|
| **Bioavailability** | F = AUC_oral / AUC_iv | Oral absorption efficiency |
| **Clearance** | CL = Dose / AUC | Drug elimination rate |
| **Half-life** | t½ = 0.693 × Vd / CL | Dosing frequency |
| **Volume of Distribution** | Vd = Dose / C₀ | Tissue penetration |

## Druggable Target Families

| Family | % of Drug Targets | Quantum Relevance |
|--------|-------------------|-------------------|
| **GPCRs** | ~30% | Binding pocket simulation |
| **Kinases** | ~20% | ATP-site selectivity modeling |
| **Ion channels** | ~15% | Conductance/gating dynamics |
| **Proteases** | ~10% | Transition state calculations |
| **Nuclear receptors** | ~10% | Ligand-binding domain energetics |

## Biomarker Categories

| Type | Purpose | Example |
|------|---------|---------|
| **Target engagement** | Confirm drug-target binding | Receptor occupancy PET |
| **Pharmacodynamic** | Pathway modulation | Phospho-protein levels |
| **Efficacy** | Clinical outcome surrogate | Tumor shrinkage |
| **Safety** | Toxicity monitoring | Liver enzymes (ALT/AST) |

## Drug Repositioning

Strategies for failed compounds:
- **Indication switching** - New disease target (e.g., thalidomide: sedative → myeloma)
- **Reformulation** - Different route/delivery
- **Off-target exploitation** - Leverage side effects as primary effect

## Drug Classes Referenced

| Class | Mechanism | Example | Data File |
|-------|-----------|---------|-----------|
| **Kinase inhibitors** | Block ATP binding in cell signaling enzymes | Imatinib, Gefitinib | `kinase_inhibitors.csv` |
| **GPCR ligands** | Modulate G protein-coupled receptors | Morphine, Risperidone | `gpcr_ligands.csv` |
| **Antibiotics** | Target bacterial cell wall, ribosomes, DNA | Amoxicillin, Ciprofloxacin | `antibiotics.csv` |
| **NSAIDs** | Inhibit COX enzymes reducing inflammation | Ibuprofen, Naproxen | — |
| **Monoclonal antibodies** | Bind specific epitopes; block or tag targets | Trastuzumab, Pembrolizumab | — |
| **Checkpoint inhibitors** | Block PD-1/CTLA-4 to restore T-cell activity | Nivolumab, Ipilimumab | — |

## Biologics & Immunotherapy

*Reference: Roitt's Essential Immunology, 13th Edition (Delves, Martin, Burton, Roitt)*

### Antibody Structure

| Term | Definition |
|------|------------|
| **Fab** | Fragment antigen-binding - variable regions (VH + VL) that contact antigen |
| **Fc** | Fragment crystallizable - constant region; mediates effector functions (ADCC, CDC) |
| **CDR** | Complementarity-determining regions - 3 hypervariable loops per V domain; direct antigen contact |
| **Kd (antibody)** | Dissociation constant; therapeutic mAbs typically 0.1-10 nM |

### Immune Checkpoints (Tumor Immunology)

| Target | Normal Function | Drug Example |
|--------|-----------------|--------------|
| **PD-1/PD-L1** | Prevents autoimmunity; tumor hijacks to evade T-cells | Pembrolizumab, Atezolizumab |
| **CTLA-4** | Downregulates T-cell activation at priming | Ipilimumab |

### Key Immunology Pathways

| Pathway | Function | Drug Target Example |
|---------|----------|---------------------|
| **JAK-STAT** | Cytokine signal transduction (IL-2, IL-6, IFN) | Tofacitinib (JAK inhibitor) |
| **NF-κB** | Inflammatory gene transcription | Bortezomib (indirect) |
| **Complement** | Innate immunity, ADCC enhancement | Eculizumab (C5 inhibitor) |

### Biologics PK Differences

| Parameter | Small Molecule | Biologic (mAb) |
|-----------|----------------|----------------|
| **MW** | <500 Da | ~150 kDa |
| **Half-life** | Hours | Weeks (FcRn recycling) |
| **Route** | Oral | IV/SC injection |
| **Immunogenicity** | Rare | ADA risk (anti-drug antibodies) |
| **TMDD** | No | Target-mediated drug disposition |

## Metabolism (CYP450)

The `ReactionPathway.fsx` example models drug metabolism by CYP450 liver enzymes:
- **CYP3A4** - metabolizes ~50% of drugs
- **CYP2D6** - polymorphic (genetic variation affects drug response)
- Common reactions: hydroxylation, N-dealkylation, oxidation

### CYP2D6 Polymorphisms (Pharmacogenomics)

| Phenotype | Population | Effect |
|-----------|------------|--------|
| Poor metabolizer | 5-10% | Drug accumulation, toxicity risk |
| Intermediate | 10-15% | Reduced clearance |
| Extensive (normal) | 70-80% | Standard response |
| Ultra-rapid | 1-10% | Reduced efficacy, need higher dose |

*Affected drugs: codeine, tamoxifen, metoprolol, many antidepressants*

## Development Phases

| Phase | Goal | Success Rate |
|-------|------|--------------|
| Target validation | Confirm disease relevance | — |
| Hit finding (HTS) | Identify active compounds | Z' > 0.5 |
| Lead optimization | Improve potency/selectivity/ADME | ~30% |
| Preclinical | Safety in animals | ~60% |
| Phase I | Safety in humans | ~70% |
| Phase II | Efficacy signal | ~30% |
| Phase III | Confirm efficacy | ~60% |

## Enzyme Kinetics (Michaelis-Menten)

| Term | Definition |
|------|------------|
| **Km** | Michaelis constant - substrate concentration at half Vmax; lower = higher affinity |
| **Vmax** | Maximum reaction velocity when enzyme is saturated |
| **kcat** | Turnover number - reactions per second per enzyme |
| **kcat/Km** | Catalytic efficiency - approaches diffusion limit (~10⁸ M⁻¹s⁻¹) for perfect enzymes |

**Inhibition Types** (drug design context):
- **Competitive**: Blocks active site → Km increases, Vmax unchanged
- **Non-competitive**: Binds allosteric site → Vmax decreases, Km unchanged
- **Uncompetitive**: Binds enzyme-substrate complex → both Km and Vmax decrease

## Protein Folding (Quantum Computing Frontier)

The **Levinthal Paradox**: A 100-residue protein has ~10⁴⁷ possible conformations.
Exhaustive classical search at 100 fs/conformation would take 10²⁷ years.
Yet proteins fold in milliseconds—nature exploits energy landscape funneling.

| Concept | Quantum Relevance |
|---------|-------------------|
| **Folding landscape** | VQE can explore energy surfaces exponentially faster |
| **Anfinsen's dogma** | Sequence determines structure → structure prediction from sequence |
| **Chaperones** (GroEL/GroES) | Assist misfolded proteins; relevant for aggregation disease modeling |

*Protein folding is one of Feynman's original motivations for quantum computing.*

## Biochemistry Foundation (Harper's Illustrated Biochemistry References)

The following chapters from *Harper's Illustrated Biochemistry* (28th Edition, Murray et al.)
provide essential biochemical background for understanding quantum drug discovery applications:

### Enzyme Kinetics & Catalysis (Quantum Transition State Modeling)

| Chapter | Topic | Quantum Relevance |
|---------|-------|-------------------|
| **Ch.7** | Enzymes: Mechanism of Action | Transition state theory, activation energy, catalytic mechanisms |
| **Ch.8** | Enzymes: Kinetics | Michaelis-Menten kinetics, Km, Vmax, kcat - parameters for quantum enzyme simulation |

*Key concepts for `BindingAffinity.fsx` and `ReactionPathway.fsx`*

### Electron Transfer & Bioenergetics (Quantum Tunneling Simulation)

| Chapter | Topic | Quantum Relevance |
|---------|-------|-------------------|
| **Ch.11** | Bioenergetics: The Role of ATP | Energy coupling, proton gradients, ATP synthase mechanism |
| **Ch.12** | Biologic Oxidation | Redox potentials, cytochromes, Fe2+/Fe3+ transitions |
| **Ch.13** | The Respiratory Chain & Oxidative Phosphorylation | Electron transport complexes, quantum tunneling in biology |

*Key concepts for `../Chemistry/ElectronTransportChain.fsx`*

### Drug Metabolism (CYP450 Quantum Chemistry)

| Chapter | Topic | Quantum Relevance |
|---------|-------|-------------------|
| **Ch.53** | Metabolism of Xenobiotics | Cytochrome P450 enzymes, Phase I/II metabolism, drug-drug interactions |

*Key concepts for `ReactionPathway.fsx` and `ADMETPrediction.fsx`*

### Protein Structure & Folding (Quantum Optimization)

| Chapter | Topic | Quantum Relevance |
|---------|-------|-------------------|
| **Ch.5** | Proteins: Higher Orders of Structure | Secondary/tertiary structure, folding, conformational dynamics |
| **Ch.6** | Proteins: Myoglobin & Hemoglobin | Heme proteins, oxygen binding, cooperative effects |

*Key concepts for `ProteinStructure.fsx` and protein folding algorithms*

### Free Radicals & Oxidative Chemistry (Electron Spin States)

| Chapter | Topic | Quantum Relevance |
|---------|-------|-------------------|
| **Ch.45** | Free Radicals and Antioxidant Nutrients | Superoxide, radical chemistry, electron spin |

*Key concepts for radical intermediate modeling in drug metabolism*

---

## Formulation & Pharmaceutics

*Reference: Aulton's Pharmaceutics: The Design and Manufacture of Medicines, 5th Edition (Aulton & Taylor)*

Quantum-predicted molecular properties must translate into real-world drug formulations.
Pharmaceutics bridges the gap between drug discovery and patient delivery.

### Biopharmaceutics Classification System (BCS)

Predicts oral absorption based on solubility and intestinal permeability (FDA guidance):

| Class | Solubility | Permeability | Absorption | Example |
|-------|------------|--------------|------------|---------|
| **I** | High | High | Well absorbed | Metoprolol, Propranolol |
| **II** | Low | High | Dissolution-limited | Nifedipine, Ketoconazole |
| **III** | High | Low | Permeability-limited | Cimetidine, Acyclovir |
| **IV** | Low | Low | Poorly absorbed | Furosemide, Taxol |

**Thresholds:**
- High solubility: Highest dose dissolves in ≤250 mL water (pH 1-7.5)
- High permeability: ≥90% absorption in humans

*Class II and IV compounds benefit most from formulation optimization (particle size, solid dispersions, etc.)*

### Dissolution & the Noyes-Whitney Equation

Drug dissolution is often the rate-limiting step for oral absorption:

```
dm/dt = kA(Cs - C) / h
```

| Symbol | Meaning | Formulation Impact |
|--------|---------|-------------------|
| **dm/dt** | Dissolution rate | What we want to maximize |
| **k** | Diffusion coefficient | Affected by viscosity of GI fluids |
| **A** | Surface area | ↑ by particle size reduction (micronization) |
| **Cs** | Saturation solubility | ↑ by salt forms, amorphous dispersions |
| **C** | Bulk concentration | Kept low by rapid absorption ("sink conditions") |
| **h** | Boundary layer thickness | ↓ by GI motility/agitation |

**Intrinsic Dissolution Rate (IDR):**
- IDR = k × Cs (surface area and agitation controlled)
- IDR < 0.1 mg·cm⁻²·min⁻¹ → dissolution-rate-limited absorption
- Measured using rotating/static disc method

*Relevant to `ADMETPrediction.fsx` - quantum-predicted solubility affects BCS class*

### Polymorphism & Solid State

Same molecule can crystallize in different forms with different properties:

| Form | Characteristics | Impact |
|------|----------------|--------|
| **Stable polymorph** | Lowest energy, most thermodynamically stable | Reference form for development |
| **Metastable polymorph** | Higher energy, faster dissolution | May convert to stable form on storage |
| **Amorphous** | No crystal lattice, highest energy | Fastest dissolution but stability concerns |
| **Hydrates/Solvates** | Solvent molecules in crystal | Different solubility than anhydrous |

**Key examples:**
- Ritonavir: Polymorph conversion caused market withdrawal (1998)
- Chloramphenicol palmitate: Polymorph B is therapeutically inactive
- Insulin zinc: Amorphous = fast onset; Crystalline = prolonged action

*Crystal form selection affects bioavailability predictions in quantum screening*

### pH-Partition Hypothesis

Drug ionization affects membrane permeation (Henderson-Hasselbalch):

```
For acids:  pH = pKa + log([A⁻]/[HA])
For bases:  pH = pKa + log([B]/[BH⁺])
```

| GI Region | pH | Implication |
|-----------|-----|-------------|
| Stomach | 1-3 | Weak acids (pKa 3-5) un-ionized → absorbed |
| Duodenum | 4-6 | Both weak acids and bases can absorb |
| Jejunum/Ileum | 6-7.5 | Weak bases (pKa 7-9) un-ionized → absorbed |

**Un-ionized fraction crosses membranes** (lipid bilayer permeability)

*Relevant to quantum LogP/pKa predictions in `ADMETPrediction.fsx`*

### Dosage Form Design Considerations

| Route | Key Factors | Quantum-Predicted Properties |
|-------|-------------|------------------------------|
| **Oral** | Dissolution, stability, first-pass metabolism | Solubility, LogP, CYP substrates |
| **Parenteral** | Solubility in aqueous vehicles, sterility | Aqueous solubility, stability |
| **Pulmonary** | Particle size (1-5 μm optimal), dissolution in lung fluid | MW, solubility, permeability |
| **Transdermal** | Lipophilicity (LogP 1-3 optimal), MW <500 | LogP, MW, H-bond donors |
| **CNS targeting** | BBB permeability | PSA <90, MW <450, LogP 1-3 |

### Formulation Strategies for Poorly Soluble Drugs

When quantum screening identifies promising but poorly soluble candidates:

| Strategy | Mechanism | Example |
|----------|-----------|---------|
| **Salt formation** | Increase dissolution rate via counterion | Diclofenac sodium vs. acid |
| **Particle size reduction** | Increase surface area (A in Noyes-Whitney) | Micronized griseofulvin |
| **Solid dispersions** | Molecular dispersion in polymer matrix | Amorphous tacrolimus in HPMC |
| **Complexation** | Cyclodextrin inclusion complexes | Itraconazole-HP-β-CD |
| **Lipid formulations** | Solubilization in lipid vehicles (SEDDS/SMEDDS) | Cyclosporine Neoral® |
| **Cocrystals** | New crystal form with coformer | Entresto® (sacubitril-valsartan) |

### Aulton's Pharmaceutics - Key Chapters

| Chapter | Topic | Relevance to Drug Discovery |
|---------|-------|----------------------------|
| **Ch.1** | Design of Dosage Forms | Translating molecular properties to products |
| **Ch.2** | Dissolution and Solubility | Noyes-Whitney, intrinsic dissolution |
| **Ch.8** | Solid-State Properties | Polymorphism, amorphous forms |
| **Ch.18-21** | Biopharmaceutics | BCS, drug absorption, bioavailability |
| **Ch.23** | Preformulation | Property characterization for formulation |

---

## Quantum Simulation Methods

### Trotter-Suzuki Decomposition

The **Trotter-Suzuki decomposition** is a fundamental technique for simulating quantum systems
on quantum computers. It enables simulation of Hamiltonians that cannot be directly implemented
as single quantum operations.

**The Problem:**
A molecular Hamiltonian H is typically a sum of many terms: H = H₁ + H₂ + ... + Hₖ
We want to simulate time evolution: U(t) = e^(-iHt)
But e^(-i(H₁+H₂)t) ≠ e^(-iH₁t) · e^(-iH₂t) when H₁ and H₂ don't commute!

**The Solution (Trotter Formula):**
Break the evolution into small steps and alternate between terms:

```
e^(-iHt) ≈ [e^(-iH₁Δt) · e^(-iH₂Δt) · ... · e^(-iHₖΔt)]^n
```

where Δt = t/n and n is the number of Trotter steps.

**Intuitive Analogy (from "Learn Quantum Computing with Python and Q#", Ch.10):**

Imagine walking from point A to point B in a city:
- **Phoenix (grid streets)**: You can only walk north-south OR east-west, not diagonally
- **Minneapolis (diagonal streets)**: You can walk northeast directly

If you want to go northeast in Phoenix, you could:
1. Walk north 10 blocks, then east 10 blocks → ends up northeast but path is L-shaped
2. Alternate: north 1 block, east 1 block, repeat 10 times → path approximates diagonal!

The more frequently you alternate (more Trotter steps), the closer your path approximates
the true diagonal. Similarly, rapidly alternating between Hamiltonian terms approximates
evolving under the full Hamiltonian.

**Orders of Approximation:**
- **1st order**: Error O(t²/n) - simple alternation
- **2nd order**: Error O(t³/n²) - symmetric splitting (forward then backward)

**Library Implementation:**
- Module: `FSharp.Azure.Quantum.Algorithms.TrotterSuzuki`
- Example: `../Chemistry/HamiltonianTimeEvolution.fsx`
- Configuration: `TrotterSteps` (accuracy) and `TrotterOrder` (1 or 2)

**Reference:**
- Kaiser, Sarah and Granade, Christopher. "Learn Quantum Computing with Python and Q#",
  Manning Publications, 2021. Chapter 10: Solving chemistry problems with quantum computers.
  https://www.manning.com/books/learn-quantum-computing-with-python-and-q-sharp

---

### VQE vs Phase Estimation for Ground State Energy

Two main quantum algorithms can find molecular ground state energies:

| Aspect | **VQE** (Variational Quantum Eigensolver) | **Phase Estimation** (QPE) |
|--------|-------------------------------------------|----------------------------|
| **Approach** | Hybrid quantum-classical optimization | Pure quantum algorithm |
| **Circuit depth** | Shallow (NISQ-friendly) | Deep (requires error correction) |
| **Measurements** | Many repeated measurements | Fewer measurements needed |
| **Classical cost** | Optimizer iterations | Minimal post-processing |
| **Hardware** | Current NISQ devices | Fault-tolerant QC required |
| **Accuracy** | Limited by ansatz expressibility | Exponentially precise |

**This library uses VQE** because:
1. Works on today's noisy quantum hardware (NISQ era)
2. Shallow circuits reduce decoherence errors
3. Flexible ansätze (UCCSD, HEA) can be tuned per problem
4. Classical optimizer handles noise gracefully

**Phase Estimation** (as in the "Learn QC with Python and Q#" book, Ch.10):
1. Prepares approximate ground state
2. Uses controlled Hamiltonian evolution (Trotter-Suzuki)
3. Applies inverse QFT to extract energy as a phase
4. Requires deep circuits → needs fault-tolerant hardware

**Both solve the same problem**: finding the lowest eigenvalue of the molecular Hamiltonian.
VQE is practical today; Phase Estimation will be superior on future fault-tolerant machines.

**Library Examples:**
- VQE approach: `../Chemistry/H2Molecule.fsx`, `BindingAffinity.fsx`
- Trotter-Suzuki (used in both): `../Chemistry/HamiltonianTimeEvolution.fsx`
- Phase Estimation module: `FSharp.Azure.Quantum.Algorithms.PhaseEstimation`

---

## Further Reading

**Example Files in this Directory:**
- `BindingAffinity.fsx` - VQE for protein-ligand binding energy (see Harper's Ch.7-8)
- `ReactionPathway.fsx` - CYP450 metabolism modeling (see Harper's Ch.53)
- `MolecularSimilarity.fsx` - Quantum kernel virtual screening
- `CaffeineEnergy.fsx` - Fragment Molecular Orbital approach
- `ProteinStructure.fsx` - PDB parsing and binding site analysis (see Harper's Ch.5-6)
- `ADMETPrediction.fsx` - Quantum ML for ADMET properties (see Harper's Ch.53)
- `NetworkPathway.fsx` - MaxCut for key drug target identification
- `CombinatorialScreening.fsx` - Knapsack for compound selection
- `DruggabilityScoring.fsx` - Graph Coloring for pharmacophore selection

**Chemistry Examples (related):**
- `../Chemistry/ElectronTransportChain.fsx` - Quantum simulation of electron tunneling (see Harper's Ch.12-13)

**Compound Libraries:**
- `kinase_inhibitors.csv` - 20 approved kinase drugs with targets
- `gpcr_ligands.csv` - 20 GPCR-targeting compounds
- `antibiotics.csv` - 20 antibiotic scaffolds by mechanism
- `library_tiny.csv` - 10 general screening candidates
- `actives_tiny.csv` - 3 known active kinase inhibitors

**External Resources:**
- RCSB PDB: https://www.rcsb.org/ (protein structures)
- ChEMBL: https://www.ebi.ac.uk/chembl/ (bioactivity data)
- DrugBank: https://go.drugbank.com/ (drug information)

**Textbook References:**
- Harper's Illustrated Biochemistry, 28th Edition (Murray et al.) - ISBN: 978-0-07-162591-3
- Roitt's Essential Immunology, 13th Edition (Delves et al.) - ISBN: 978-1-118-41577-1
- Aulton's Pharmaceutics, 5th Edition (Aulton & Taylor) - ISBN: 978-0-7020-7005-1
