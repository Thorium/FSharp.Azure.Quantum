# Bundled FCIDUMP integrals for the chemistry examples

Molecular integrals the chemistry examples (`AntibioticPrecursorSynthesis.fsx`,
`BindingAffinity.fsx`, `ElectronTransportChain.fsx`, `H2OWater.fsx`) run VQE on by default.
Each species has `<slug>.fcidump` (active-space Hamiltonian, STO-3G, core energy included
as the `0 0 0 0` line) and `<slug>.xyz` (the geometry it was computed at, in Angstrom).
The slug is the species name lower-cased with every run of other characters replaced by
`-` (`ChemistryIntegrals.speciesSlug` in `examples/_common`). `manifest.json` lists every
file with its geometry, active space and energies.

## Regenerating

`generate-fcidumps.fsx` in this folder regenerates every file here, this README and
`manifest.json` included, by the method below:

    dotnet fsi generate-fcidumps.fsx                  # everything, TS searches included (~10 min)
    dotnet fsi generate-fcidumps.fsx -- --check       # regenerate into a temporary folder and compare
    dotnet fsi generate-fcidumps.fsx -- --only kinugasa-ts --out DIR
    dotnet fsi generate-fcidumps.fsx -- --docs-only   # this README and the manifest only

It needs a Python with PySCF, SciPy and geomeTRIC (`--python PATH`; otherwise `python` or
`python3` on the PATH, then `~/pyscf-venv/bin/python` in WSL). The quantum chemistry runs
there as short PySCF snippets, one thread per process; the script itself does the rest:
the steps below, the file formats, the transition-state validation, this README and the
manifest. The species, their starting geometries and active spaces, and the
transition-state starts and constraints are listed at the top of the script.

`--check` compares every regenerated file with the bundled one: the integrals as written
and after the best choice of orbital signs, the four lowest eigenvalues of the two
active-space Hamiltonians (unchanged by any rotation of the active orbitals), the geometry
and every manifest value. With the versions below, E(RHF) and E(CAS) reproduce to about
1e-9 Ha and most integrals to 1e-8. Where the CASSCF optimum is flat or its orbitals
degenerate (linear or symmetric molecules, the hydrogen-bonded complexes), another run
can settle on other active orbitals with the same E(CAS), and threaded PySCF does so from
run to run; the script therefore runs one thread per process. `--check` names each file
a rerun changes. Other PySCF versions give slightly different numbers.

## How they were made

All files were computed with PySCF 2.14.0 (SciPy 1.18.1 for the
minimisations, geomeTRIC 1.1.1 for the transition states) in the STO-3G
basis on a closed-shell RHF reference, SCF converged to 1e-11 Ha (1e-10 Ha inside the
geomeTRIC optimisations).

1. Geometry. Each species is a minimum of the RHF/STO-3G energy, found from PySCF's
   analytic nuclear gradients with SciPy's BFGS (gradient tolerance 1e-5 Ha/bohr; a
   result with any gradient component above 1e-4 Ha/bohr is rejected), starting from
   hand-built structures. The analytic RHF Hessian (`pyscf.hessian.thermo`) then
   checks that each is a minimum. Where a frequency was imaginary beyond 30i cm-1 (a
   saddle point: the planar 2-azetidinone and 1,4-dihydropyridine rings, which pucker,
   and the planar HF-H2O complex), the structure was displaced 0.1 A along that mode
   and minimised again until no such frequency remained (which way along the mode is the
   sign LAPACK gives the eigenvector, so a rerun elsewhere may pucker a ring the other
   way: a mirror image with the same energies). The table gives each
   geometry's lowest frequency. Stretched species take the equilibrium geometry with
   one X-H bond scaled and are not re-optimised, so they are not stationary points.
2. Active space. CASSCF(n,n) in PySCF (`mcscf.CASSCF`, convergence 1e-10 Ha, up to 200
   macro-iterations) is run from several starting orbital sets and the lowest converged
   energy is kept. The starts are the canonical RHF frontier orbitals (the top n/2
   occupied and the bottom n/2 virtual), the MP2 natural orbitals with the occupations
   furthest from 2 and 0 (the n/2 least-occupied occupied-type and the n/2 most-occupied
   virtual-type), and, for the hydrogen-bonded complexes, the complex orbitals that
   overlap most with each monomer's HOMO and LUMO. Bond-dissociation species start only
   from the sigma/sigma* pair of the bond (the occupied and the virtual canonical orbital
   with the largest hydrogen 1s weight), so both geometries of a molecule share the same
   active orbitals. Orbital optimisation also removes the arbitrary rotation inside
   degenerate shells (the pi orbitals of linear molecules).
3. Balance. Every reaction in the examples keeps the same total active space on both
   sides: two CAS(2,2) reactants form a CAS(4,4) product, a CAS(4,4) reactant forms two
   CAS(2,2) products, and a transition state has the reactants' total.
4. Export. A CASCI on the optimised CASSCF orbitals gives the active-space Hamiltonian:
   the one-electron integrals with the doubly occupied core folded in and the
   two-electron integrals of the active orbitals. They are written in the layout of
   `pyscf.tools.fcidump.from_integrals` (4-fold symmetry, values above 1e-15), with
   MS2 = 0 and the core energy (nuclear repulsion plus the frozen-core electronic
   energy) as the `0 0 0 0` line.

`E(CAS)` is the exact ground-state energy of the active-space Hamiltonian (the CASSCF
energy), which a converged VQE reproduces. `E(CAS) - E(RHF)` is the energy the active
space adds: correlation plus orbital relaxation.

## Transition states and activation energies

`AntibioticPrecursorSynthesis.fsx` reports `Ea = E(TS) - sum E(reactants)`, from VQE on
every species, for each route whose transition state has an FCIDUMP in the integral
folder (this one or `--fcidump-dir`): the TS species is named `<route name> TS`, so the
file is `<route-slug>-ts.fcidump` (e.g. `kinugasa-ts.fcidump`). Routes without one report
their reaction energy only. The bundled transition states were made as follows, and the
same steps (a new entry in the script's transition-state list) add one for another route
or for your own reaction:

1. Guess. Start from the product (plus any leaving molecule) or from the reactants placed
   face to face, and minimise at RHF/STO-3G with the forming and breaking bond lengths
   held near their expected TS values (geomeTRIC distance constraints: a `$set` block
   passed as `constraints=` to `pyscf.geomopt.geometric_solver.optimize`).
2. Saddle point. `optimize(mf, transition=True, hessian="first")` runs geomeTRIC's
   transition-state optimiser from that guess, starting from PySCF's analytic Hessian.
3. Validation. The analytic RHF Hessian at the result has exactly one imaginary
   frequency, and that mode is the reaction coordinate of the step: minimising from the
   TS displaced 0.15 A (largest atomic displacement) either way along it ends once in the
   product basin (every forming bond below 1.7 A, every breaking bond above 1.8 A) and
   once in the reactant basin (the other way round). A TS whose downhill path ends in an
   intermediate belongs to another step and is rejected. `manifest.json` records both
   downhill ends.
4. FCIDUMP. The active space is chosen and exported as above, with the reactants' total
   (electrons, orbitals): CAS(4,4) for two CAS(2,2) reactants or one CAS(4,4) reactant.
   Ea then compares like with like.

Each search is limited to 15 minutes of wall-clock time, search and validation together.
geomeTRIC's default convergence leaves a saddle point uncertain by about 2e-3 A, so a
rerun lands on the same saddle point but not the same digits. A search that reaches the
bundled TS's saddle point (every interatomic distance within 0.05 A) keeps the bundled
geometry, validated again, so its files reproduce; a new saddle point replaces it once
validated. When a search fails or runs out of time, the bundled geometry is validated
instead, and the script says which path each TS took. A route whose TS validates
neither way has no TS file. The ring expansion (aziridine + CO) has none: its
CO-insertion search did not converge. No TS energy is estimated or interpolated.

| Route TS | Elementary step | Imaginary frequency | Ea from E(RHF) / kcal/mol | Ea from E(CAS) / kcal/mol |
|---|---|---|---|---|
| `staudinger-2-2-ts.fcidump` | ketene + methanimine -> 2-azetidinone | 924i cm-1 | 45.1 | 32.0 |
| `kinugasa-ts.fcidump` | nitrone + acetylene -> 4-isoxazoline (first step; the rearrangement to the lactam follows) | 551i cm-1 | 12.2 | 14.7 |
| `beta-amino-acid-cyclization-ts.fcidump` | beta-alanine -> 2-azetidinone + water | 1390i cm-1 | 83.8 | 98.2 |

Barriers from a minimal basis without dynamic correlation are too high, typically by
tens of kcal/mol, and the steps are uncatalysed and in the gas phase (Kinugasa uses Cu,
carbonylation Co or Rh, lactamisation an activating agent). Read them as a qualitative
ordering of these model steps.

## What an active-space energy difference means

Every energy here is an STO-3G total energy: mean-field inactive orbitals plus exact
correlation inside a small active space. Differences between species are therefore
close to RHF/STO-3G reaction or binding energies, plus the correlation of the active
orbitals. Because of the balance rule, the correlation added is balanced in size,
though not necessarily in character when bonds change, and CASSCF can settle in a local
minimum (ketene's CAS(2,2) adds little). Compare `E(CAS) - E(RHF)` below to see how much
of an energy difference comes from the active space. A minimal basis and a small active
space give qualitative trends: signs and orderings, not kcal/mol accuracy. Errors of
tens of kcal/mol against experiment are expected, and hydrogen-bond energies carry a
large basis-set superposition error.

## Files

| Species | File | Geometry | Active space | Qubits | E(RHF) / Ha | E(CAS) / Ha | E(CAS) - E(RHF) / Ha |
|---|---|---|---|---|---|---|---|
| Hydrogen (H2) [CAS(2,2)] | `hydrogen-h2-cas-2-2.fcidump` | optimised (max gradient 6.5e-07 Ha/bohr); lowest frequency 5481 cm-1 | lowest CASSCF(2,2) from frontier and MP2 natural orbitals | 4 | -1.11750589 | -1.13684784 | -0.019342 |
| Water [CAS(2,2)] | `water-cas-2-2.fcidump` | optimised (max gradient 5.9e-07 Ha/bohr); lowest frequency 2170 cm-1 | lowest CASSCF(2,2) from frontier and MP2 natural orbitals | 4 | -74.96590119 | -74.97995699 | -0.014056 |
| Ketene [CAS(2,2)] | `ketene-cas-2-2.fcidump` | optimised (max gradient 6.0e-06 Ha/bohr); lowest frequency 461 cm-1 | lowest CASSCF(2,2) from frontier and MP2 natural orbitals | 4 | -149.72610500 | -149.72646377 | -0.000359 |
| Methanimine [CAS(2,2)] | `methanimine-cas-2-2.fcidump` | optimised (max gradient 2.5e-06 Ha/bohr); lowest frequency 1204 cm-1 | lowest CASSCF(2,2) from frontier and MP2 natural orbitals | 4 | -92.82304307 | -92.87050711 | -0.047464 |
| Aziridine [CAS(2,2)] | `aziridine-cas-2-2.fcidump` | optimised (max gradient 5.3e-06 Ha/bohr); lowest frequency 919 cm-1 | lowest CASSCF(2,2) from frontier and MP2 natural orbitals | 4 | -131.39947635 | -131.41731818 | -0.017842 |
| Carbon monoxide [CAS(2,2)] | `carbon-monoxide-cas-2-2.fcidump` | optimised (max gradient 7.0e-06 Ha/bohr); lowest frequency 2462 cm-1 | lowest CASSCF(2,2) from frontier and MP2 natural orbitals | 4 | -111.22544951 | -111.25358698 | -0.028137 |
| Formaldonitrone [CAS(2,2)] | `formaldonitrone-cas-2-2.fcidump` | optimised (max gradient 2.5e-06 Ha/bohr); lowest frequency 550 cm-1 | lowest CASSCF(2,2) from frontier and MP2 natural orbitals | 4 | -166.56679019 | -166.63795116 | -0.071161 |
| Acetylene [CAS(2,2)] | `acetylene-cas-2-2.fcidump` | optimised (max gradient 4.6e-06 Ha/bohr); lowest frequency 945 cm-1 | lowest CASSCF(2,2) from frontier and MP2 natural orbitals | 4 | -75.85624771 | -75.88585555 | -0.029608 |
| beta-Alanine [CAS(4,4)] | `beta-alanine-cas-4-4.fcidump` | optimised (max gradient 6.7e-06 Ha/bohr); lowest frequency 40 cm-1 | lowest CASSCF(4,4) from frontier and MP2 natural orbitals | 8 | -317.69920234 | -317.77740625 | -0.078204 |
| 2-Azetidinone [CAS(2,2)] | `2-azetidinone-cas-2-2.fcidump` | optimised, minimum verified (lowest frequency 205 cm-1) | lowest CASSCF(2,2) from frontier and MP2 natural orbitals | 4 | -242.67750682 | -242.72186147 | -0.044355 |
| 2-Azetidinone [CAS(4,4)] | `2-azetidinone-cas-4-4.fcidump` | optimised, minimum verified (lowest frequency 205 cm-1) | lowest CASSCF(4,4) from frontier and MP2 natural orbitals | 8 | -242.67750682 | -242.75453523 | -0.077028 |
| Pyridine [CAS(2,2)] | `pyridine-cas-2-2.fcidump` | optimised (max gradient 7.5e-06 Ha/bohr); lowest frequency 460 cm-1 | lowest CASSCF(2,2) from frontier and MP2 natural orbitals | 4 | -243.63861356 | -243.66219772 | -0.023584 |
| 1,4-Dihydropyridine [CAS(4,4)] | `1-4-dihydropyridine-cas-4-4.fcidump` | optimised, minimum verified (lowest frequency 155 cm-1) | lowest CASSCF(4,4) from frontier and MP2 natural orbitals | 8 | -244.78479007 | -244.86160855 | -0.076818 |
| Ethylene [CAS(2,2)] | `ethylene-cas-2-2.fcidump` | optimised (max gradient 6.1e-07 Ha/bohr); lowest frequency 947 cm-1 | lowest CASSCF(2,2) from frontier and MP2 natural orbitals | 4 | -77.07395478 | -77.11519299 | -0.041238 |
| Ethane [CAS(4,4)] | `ethane-cas-4-4.fcidump` | optimised (max gradient 7.6e-06 Ha/bohr); lowest frequency 317 cm-1 | lowest CASSCF(4,4) from frontier and MP2 natural orbitals | 8 | -78.30617965 | -78.33984371 | -0.033664 |
| p-Benzoquinone [CAS(2,2)] | `p-benzoquinone-cas-2-2.fcidump` | optimised (max gradient 5.1e-06 Ha/bohr); lowest frequency 86 cm-1 | lowest CASSCF(2,2) from frontier and MP2 natural orbitals | 4 | -374.35334790 | -374.37346429 | -0.020116 |
| Hydroquinone [CAS(4,4)] | `hydroquinone-cas-4-4.fcidump` | optimised (max gradient 5.9e-06 Ha/bohr); lowest frequency 178 cm-1 | lowest CASSCF(4,4) from frontier and MP2 natural orbitals | 8 | -375.57292272 | -375.62477869 | -0.051856 |
| Hydrogen peroxide [CAS(2,2)] | `hydrogen-peroxide-cas-2-2.fcidump` | optimised (max gradient 4.7e-06 Ha/bohr); lowest frequency 184 cm-1 | lowest CASSCF(2,2) from frontier and MP2 natural orbitals | 4 | -148.76499662 | -148.80380304 | -0.038806 |
| Hydrogen fluoride [CAS(2,2)] | `hydrogen-fluoride-cas-2-2.fcidump` | optimised (max gradient 2.1e-06 Ha/bohr); lowest frequency 4474 cm-1 | lowest CASSCF(2,2) from frontier and MP2 natural orbitals | 4 | -98.57284735 | -98.59989077 | -0.027043 |
| Hydrogen sulfide [CAS(2,2)] | `hydrogen-sulfide-cas-2-2.fcidump` | optimised (max gradient 3.7e-06 Ha/bohr); lowest frequency 1610 cm-1 | lowest CASSCF(2,2) from frontier and MP2 natural orbitals | 4 | -394.31163006 | -394.32053317 | -0.008903 |
| Hydrogen chloride [CAS(2,2)] | `hydrogen-chloride-cas-2-2.fcidump` | optimised (max gradient 1.4e-06 Ha/bohr); lowest frequency 3372 cm-1 | lowest CASSCF(2,2) from frontier and MP2 natural orbitals | 4 | -455.13601165 | -455.15538730 | -0.019376 |
| HF dimer (F-H...F) [CAS(4,4)] | `hf-dimer-f-h-f-cas-4-4.fcidump` | optimised (max gradient 8.9e-06 Ha/bohr); lowest frequency 275 cm-1 | lowest CASSCF(4,4) from frontier, MP2 natural and monomer-matched orbitals | 8 | -197.15447571 | -197.20465623 | -0.050181 |
| HF-H2S (F-H...S) [CAS(4,4)] | `hf-h2s-f-h-s-cas-4-4.fcidump` | optimised (max gradient 1.1e-06 Ha/bohr); lowest frequency 154 cm-1 | lowest CASSCF(4,4) from frontier, MP2 natural and monomer-matched orbitals | 8 | -492.88962412 | -492.92754368 | -0.037920 |
| HF-HCl (F-H...Cl) [CAS(4,4)] | `hf-hcl-f-h-cl-cas-4-4.fcidump` | optimised (max gradient 4.8e-06 Ha/bohr); lowest frequency 76 cm-1 | lowest CASSCF(4,4) from frontier, MP2 natural and monomer-matched orbitals | 8 | -553.71092862 | -553.75654409 | -0.045615 |
| HF-H2O (F-H...O) [CAS(4,4)] | `hf-h2o-f-h-o-cas-4-4.fcidump` | optimised, minimum verified (lowest frequency 264 cm-1) | lowest CASSCF(4,4) from frontier, MP2 natural and monomer-matched orbitals | 8 | -173.55071515 | -173.59968072 | -0.048966 |
| H2O (equilibrium) | `h2o-equilibrium.fcidump` | optimised (max gradient 2.1e-07 Ha/bohr); lowest frequency 2170 cm-1 | CASSCF(2,2) from the sigma/sigma* orbitals of bond O1-H2 | 4 | -74.96590119 | -74.97995698 | -0.014056 |
| HF (equilibrium) | `hf-equilibrium.fcidump` | optimised (max gradient 1.3e-06 Ha/bohr); lowest frequency 4475 cm-1 | CASSCF(2,2) from the sigma/sigma* orbitals of bond F2-H1 | 4 | -98.57284735 | -98.59989072 | -0.027043 |
| LiH (equilibrium) | `lih-equilibrium.fcidump` | optimised (max gradient 1.1e-07 Ha/bohr); lowest frequency 1868 cm-1 | CASSCF(2,2) from the sigma/sigma* orbitals of bond Li1-H2 | 4 | -7.86338213 | -7.88116687 | -0.017785 |
| H2O (O-H x2.0) | `h2o-o-h-x2-0.fcidump` | H2O (equilibrium) geometry with the O-H bond scaled x2.0 | CASSCF(2,2) from the sigma/sigma* orbitals of bond O1-H2 | 4 | -74.67962375 | -74.84874600 | -0.169122 |
| HF (H-F x2.0) | `hf-h-f-x2-0.fcidump` | HF (equilibrium) geometry with the F-H bond scaled x2.0 | CASSCF(2,2) from the sigma/sigma* orbitals of bond F2-H1 | 4 | -98.27861440 | -98.47046581 | -0.191851 |
| LiH (Li-H x2.0) | `lih-li-h-x2-0.fcidump` | LiH (equilibrium) geometry with the Li-H bond scaled x2.0 | CASSCF(2,2) from the sigma/sigma* orbitals of bond Li1-H2 | 4 | -7.70842334 | -7.79763984 | -0.089217 |
| Staudinger [2+2] TS | `staudinger-2-2-ts.fcidump` | TS optimised with geomeTRIC, imaginary frequency 924i cm-1 | lowest CASSCF(4,4) from frontier and MP2 natural orbitals | 8 | -242.47733411 | -242.54594265 | -0.068609 |
| Kinugasa TS | `kinugasa-ts.fcidump` | TS optimised with geomeTRIC, imaginary frequency 551i cm-1 | lowest CASSCF(4,4) from frontier and MP2 natural orbitals | 8 | -242.40354048 | -242.50034298 | -0.096803 |
| beta-Amino Acid Cyclization TS | `beta-amino-acid-cyclization-ts.fcidump` | TS optimised with geomeTRIC, imaginary frequency 1390i cm-1 | lowest CASSCF(4,4) from frontier and MP2 natural orbitals | 8 | -317.56570492 | -317.62087279 | -0.055168 |

## Licence

These files are original computed output of the PySCF calculations described above,
released with the rest of this repository under the Unlicense.
