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

## Drug Classes Referenced

| Class | Mechanism | Example | Data File |
|-------|-----------|---------|-----------|
| **Kinase inhibitors** | Block ATP binding in cell signaling enzymes | Imatinib, Gefitinib | `kinase_inhibitors.csv` |
| **GPCR ligands** | Modulate G protein-coupled receptors | Morphine, Risperidone | `gpcr_ligands.csv` |
| **Antibiotics** | Target bacterial cell wall, ribosomes, DNA | Amoxicillin, Ciprofloxacin | `antibiotics.csv` |
| **NSAIDs** | Inhibit COX enzymes reducing inflammation | Ibuprofen, Naproxen | — |

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

## Further Reading

**Example Files in this Directory:**
- `BindingAffinity.fsx` - VQE for protein-ligand binding energy
- `ReactionPathway.fsx` - CYP450 metabolism modeling  
- `MolecularSimilarity.fsx` - Quantum kernel virtual screening
- `CaffeineEnergy.fsx` - Fragment Molecular Orbital approach
- `ProteinStructure.fsx` - PDB parsing and binding site analysis
- `ADMETPrediction.fsx` - Quantum ML for ADMET properties

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
