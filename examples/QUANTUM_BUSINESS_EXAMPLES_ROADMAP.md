# Quantum Business Examples Roadmap

**FSharp.Azure.Quantum - MVP-Ready Business Examples Strategy**

**Date:** January 2026  
**Status:** Revised - Focus on Proven Quantum Advantage  
**Goal:** Create examples only where quantum computing provides **real, proven advantage**

---

## Executive Summary

This document outlines a strategy for FSharp.Azure.Quantum business examples, focusing **exclusively** on areas with demonstrated quantum advantage:

1. **Quantum Chemistry for Drug Discovery** - VQE/molecular simulation (exponential advantage)
2. **Quantum Monte Carlo for Finance** - Amplitude estimation (quadratic advantage)
3. **Combinatorial Optimization** - QAOA for NP-hard problems (polynomial advantage)

**Removed from scope:** Quantum ML for classification (no proven advantage over classical ML).

---

## Quantum Advantage Reality Check

### Areas with PROVEN Quantum Advantage

| Domain | Algorithm | Speedup | Why It Works |
|--------|-----------|---------|--------------|
| **Molecular Simulation** | VQE | Exponential | Quantum systems simulate quantum systems naturally |
| **Monte Carlo Integration** | Amplitude Estimation | Quadratic O(1/ε) vs O(1/ε²) | Quantum parallelism in probability estimation |
| **Combinatorial Optimization** | QAOA/Grover | Polynomial | Quantum tunneling, amplitude amplification |
| **Linear Systems** | HHL | Exponential (sparse) | Quantum linear algebra |
| **Cryptography** | Shor | Exponential | Period finding via QFT |

### Areas WITHOUT Proven Quantum Advantage (REMOVED)

| Domain | Why No Advantage | Better Alternative |
|--------|------------------|-------------------|
| Credit Risk Scoring | Tabular data, low dimensions | Classical ML (XGBoost, logistic regression) |
| QSAR Classification | Fingerprints are classical features | Classical ML, neural networks |
| Drug-Drug Interactions | Graph ML works well classically | Classical GNN, random forests |
| Trading Signals | Pattern recognition is classical | Classical time series models |
| Fraud Detection | Feature engineering dominates | Classical ML with good features |

**Key Insight:** Quantum ML (VQC, Quantum SVM) shows no practical advantage for tabular/structured data classification on NISQ hardware. The advantage, if any, requires fault-tolerant quantum computers.

---

## Current Library Capabilities

### Algorithms with Real Quantum Advantage

| Algorithm | File | Proven Speedup | Business Application |
|-----------|------|----------------|---------------------|
| VQE | `Algorithms/VQE.fs` | Exponential | Molecular ground state energy |
| Quantum Monte Carlo | `Algorithms/QuantumMonteCarlo.fs` | Quadratic | Risk calculations, option pricing |
| QAOA | `Core/QaoaCircuit.fs` | Polynomial | Scheduling, routing, allocation |
| Grover | `Algorithms/Grover.fs` | Quadratic O(√N) | Database search, SAT solving |
| HHL | `Algorithms/HHL.fs` | Exponential (sparse) | Linear systems, regression |
| Amplitude Estimation | `Algorithms/AmplitudeAmplification.fs` | Quadratic | Probability/expectation estimation |

### Existing Examples (Already Complete)

**Quantum Chemistry:**
- `Chemistry/H2Molecule.fsx` - VQE for hydrogen molecule ✅
- `Chemistry/H2_UCCSD_VQE_Example.fsx` - UCCSD ansatz ✅
- `Chemistry/HamiltonianTimeEvolution.fsx` - Time evolution ✅
- `Chemistry/UCCSDExample.fsx` - Coupled cluster ✅

**Financial Monte Carlo:**
- `FinancialRisk/QuantumVaR.fsx` - Value-at-Risk with amplitude estimation ✅
- `QuantumOptionPricing/QuantumOptionPricing.fsx` - Option pricing ✅

**Combinatorial Optimization:**
- `GraphColoring/GraphColoring.fsx` - QAOA ✅
- `MaxCut/MaxCut.fsx` - QAOA ✅
- `JobScheduling/JobScheduling.fsx` - Constraint optimization ✅
- `InvestmentPortfolio/InvestmentPortfolio.fsx` - Portfolio optimization ✅

---

## Proposed New Examples

### Domain 1: Quantum Chemistry for Drug Discovery

**Why Quantum Advantage Exists:**
Simulating molecular systems requires exponential classical resources (electron correlation). Quantum computers simulate quantum systems naturally - this is Feynman's original motivation for quantum computing.

#### 1.1 Drug-Like Molecule Ground State
**File:** `examples/DrugDiscovery/CaffeineEnergy.fsx`

**Business Problem:**  
Calculate ground state energy of caffeine (C₈H₁₀N₄O₂) or similar drug-like molecules. Classical methods (DFT, CCSD(T)) scale poorly with molecular size.

**Quantum Advantage:**  
VQE with UCCSD ansatz can compute electron correlation energy with polynomial quantum resources vs exponential classical.

**MVP Checklist:**
- [ ] Extend existing VQE to larger molecules (>4 atoms)
- [ ] Active space selection for tractable computation
- [ ] UCCSD ansatz with configurable excitations
- [ ] Comparison with classical DFT/HF baseline
- [ ] Energy convergence visualization

---

#### 1.2 Protein-Ligand Binding Affinity
**File:** `examples/DrugDiscovery/BindingAffinity.fsx`

**Business Problem:**  
Predict how strongly a drug candidate binds to a protein target. This determines drug efficacy. Classical force fields are approximations; quantum simulation is exact.

**Quantum Advantage:**  
Quantum simulation of the binding site + ligand interaction captures electron correlation effects that classical force fields miss.

**Note:** This is aspirational for current NISQ hardware. Realistic implementation requires:
- Active space of 10-20 qubits (current limit)
- Frozen core approximation
- Fragment molecular orbital methods

**MVP Checklist:**
- [ ] Fragment-based molecular orbital approach
- [ ] Binding energy decomposition (electrostatic, dispersion, etc.)
- [ ] Comparison with classical docking scores
- [ ] Scalability analysis (when quantum advantage emerges)

---

#### 1.3 Reaction Pathway Energy Profile
**File:** `examples/DrugDiscovery/ReactionPathway.fsx`

**Business Problem:**  
Calculate energy barriers for drug metabolism reactions. Predicts how drugs are processed in the body (cytochrome P450 reactions).

**Quantum Advantage:**  
Transition state energies require accurate electron correlation - exactly where quantum excels.

**MVP Checklist:**
- [ ] Nudged elastic band or similar path optimization
- [ ] Transition state identification
- [ ] Arrhenius rate constant estimation
- [ ] Comparison with experimental activation energies

---

### Domain 2: Quantum Monte Carlo for Finance

**Why Quantum Advantage Exists:**
Monte Carlo integration achieves precision ε with O(1/ε²) classical samples. Quantum amplitude estimation achieves O(1/ε) - quadratic speedup. For 1% precision, this is 100x speedup.

#### 2.1 Quantum Value-at-Risk (EXISTING - ENHANCE)
**File:** `examples/FinancialRisk/QuantumVaR.fsx`

**Status:** ✅ Already implemented and RULE1 compliant

**Enhancements Needed:**
- [ ] Real market data integration (Yahoo Finance API pattern)
- [ ] Multi-asset correlation from historical data
- [ ] CVaR (Expected Shortfall) with quantum estimation
- [ ] Regulatory report format (Basel III template)

---

#### 2.2 Portfolio Stress Testing
**File:** `examples/FinancialRisk/StressTesting.fsx`

**Business Problem:**  
Evaluate portfolio under thousands of stress scenarios simultaneously. Required for CCAR/DFAST regulatory compliance.

**Quantum Advantage:**  
Quantum amplitude estimation can evaluate expected loss across exponentially many scenario combinations.

**MVP Checklist:**
- [ ] Multi-factor scenario encoding in quantum state
- [ ] Correlated factor simulation
- [ ] Expected shortfall via amplitude estimation
- [ ] Historical scenario replay (2008, COVID)
- [ ] CCAR report template output

---

#### 2.3 Derivative Pricing with Path Dependence
**File:** `examples/FinancialRisk/ExoticOptions.fsx`

**Business Problem:**  
Price path-dependent derivatives (Asian options, barriers, lookbacks). Classical Monte Carlo is slow for high accuracy.

**Quantum Advantage:**  
Quantum Monte Carlo achieves quadratic speedup for path integral evaluation.

**MVP Checklist:**
- [ ] Path encoding in quantum register
- [ ] Asian, barrier, lookback option support
- [ ] Greeks via finite difference on quantum prices
- [ ] Comparison with classical MC (accuracy vs queries)

---

### Domain 3: Combinatorial Optimization (EXISTING - DOCUMENT)

**Why Quantum Advantage Exists:**
QAOA and quantum annealing can find good solutions to NP-hard problems. While not exponential speedup, polynomial improvements + quantum tunneling help escape local minima.

**Already Complete:**
- `GraphColoring/GraphColoring.fsx` ✅
- `MaxCut/MaxCut.fsx` ✅
- `JobScheduling/JobScheduling.fsx` ✅
- `SupplyChain/SupplyChain.fsx` ✅
- `InvestmentPortfolio/InvestmentPortfolio.fsx` ✅
- `DeliveryRouting/DeliveryRouting.fsx` ✅

**Enhancement:** Add documentation explaining WHEN quantum advantage emerges (problem size thresholds).

---

## Removed from Roadmap

The following were in the previous roadmap but are **removed** due to lack of proven quantum advantage:

### ❌ Credit Risk Scoring
**Reason:** Tabular classification with ~20 features. Classical ML (logistic regression, XGBoost, neural nets) achieves >95% accuracy. Quantum ML shows no improvement on NISQ hardware.

**Alternative:** Use existing `BinaryClassificationBuilder` if someone wants quantum, but document that classical is likely better.

### ❌ QSAR Classification  
**Reason:** Molecular fingerprints (1024-2048 bits) are classical features. Classical random forests achieve AUC >0.85 on most datasets. No demonstrated quantum advantage.

**Alternative:** Focus on quantum chemistry (VQE) for molecular properties instead of quantum ML for classification.

### ❌ Drug-Drug Interaction Prediction
**Reason:** Graph neural networks work well. Quantum graph ML is research-stage with no practical advantage.

### ❌ Trading Signal Generation
**Reason:** Pattern recognition in time series. Classical deep learning (LSTM, Transformers) dominates. No quantum algorithm provides speedup.

---

## Implementation Roadmap (Revised)

### Phase 1: Enhance Existing (2 weeks)

**Week 1: Financial Risk Completion**
- [x] QuantumVaR.fsx - Already complete ✅
- [x] Add real market data loading pattern (Yahoo Finance CSV) ✅
- [x] Add stress testing example (extends QuantumVaR) ✅
- [ ] Document quantum speedup analysis

**Week 2: Chemistry Documentation**
- [x] H2Molecule.fsx - Already complete ✅
- [x] H2_UCCSD_VQE_Example.fsx - UCCSD ansatz ✅
- [x] Add larger molecule example (water) - H2OWater.fsx ✅
- [x] Document active space selection (in H2OWater.fsx) ✅
- [ ] Add energy convergence plots

### Phase 2: Drug Discovery Chemistry (3 weeks)

**Week 3: Molecular Energy Calculations**
- [x] CaffeineEnergy.fsx - Drug-like molecule VQE ✅
- [x] Fragment molecular orbital approach ✅
- [x] Active space optimization ✅

**Week 4: Binding Affinity**
- [x] BindingAffinity.fsx - Protein-ligand interaction ✅
- [x] Fragment-based approach for tractability ✅
- [x] Comparison with classical docking ✅

**Week 5: Reaction Pathways**
- [x] ReactionPathway.fsx - Transition state energies ✅
- [x] Drug metabolism example (CYP450 hydroxylation) ✅
- [x] Arrhenius kinetics ✅

### Phase 3: Advanced Finance (2 weeks)

**Week 6: Exotic Derivatives**
- [x] ExoticOptions.fsx - Path-dependent options ✅
- [x] Barrier options (Up/Down, In/Out) ✅
- [x] Lookback options (Floating/Fixed strike) ✅
- [x] Greeks calculation (Delta, Vega, Theta) ✅

**Week 7: Integration & Validation**
- [ ] Error mitigation integration

---

## No New Builders Needed

After careful analysis, **no new builders are required**:

| Proposed Builder | Decision | Reason |
|-----------------|----------|--------|
| DrugDiscoveryBuilder | ❌ DROP | Use existing VQE + Chemistry modules |
| QuantumRiskEngineBuilder | ❌ DROP | Use existing QuantumMonteCarlo + FinancialData |
| CreditRiskBuilder | ❌ DROP | No quantum advantage; use BinaryClassificationBuilder |

**The library already has the right abstractions:**
- `VQE` + `Chemistry/` modules → Drug discovery quantum chemistry
- `QuantumMonteCarlo` + `AmplitudeEstimation` → Financial risk
- `QAOA` + `ConstraintScheduler` → Optimization

New examples should **compose existing capabilities**, not create new builders.

---

## Success Criteria (Revised)

### Example Quality Criteria

An example demonstrates real quantum advantage when:

1. **Quantum Algorithm Required**: Problem genuinely requires quantum (simulation, amplitude estimation)
2. **Classical Comparison**: Shows concrete speedup/accuracy vs classical baseline
3. **Scalability Analysis**: Documents at what problem size quantum advantage emerges
4. **RULE1 Compliant**: All computation flows through `IQuantumBackend`
5. **Reproducible**: Deterministic results with seed control

### Removed Criteria
- ~~"MVP-ready for business"~~ → Focus on demonstrating quantum advantage first
- ~~"Real data format support"~~ → Synthetic data fine if physics is correct

---

## References

### Quantum Chemistry (Proven Advantage)
1. Peruzzo et al., "A variational eigenvalue solver on a quantum processor" (2014)
2. Google AI, "Hartree-Fock on a superconducting qubit quantum computer" (2020)
3. IBM, "Scalable quantum simulation of molecular energies" (2017)

### Quantum Monte Carlo (Proven Advantage)
1. Montanaro, "Quantum speedup of Monte Carlo methods" (2015)
2. Woerner & Egger, "Quantum risk analysis" (2019)
3. Stamatopoulos et al., "Option pricing using quantum computers" (2020)

### Quantum ML (No Proven Advantage - Cautionary)
1. Schuld & Killoran, "Is quantum advantage the right goal for QML?" (2022)
2. Tang, "Dequantization" papers showing classical algorithms match quantum ML
3. Bowles et al., "The Quantum Kernel Trick" limitations (2023)

---

## Appendix: RULE1 Compliance

> **RULE1:** "This is an Azure Quantum library, NOT a standalone solver library. All code must depend on IQuantumBackend."

All examples in this roadmap:
- ✅ Require `backend: IQuantumBackend` parameter
- ✅ Execute quantum circuits through the backend
- ✅ Use `LocalBackend` for simulation, cloud backends for real hardware
- ✅ No classical-only solvers exposed as "quantum"

---

**Document Status:** Revised for Realistic Quantum Advantage  
**Key Change:** Removed quantum ML examples; focused on quantum chemistry + Monte Carlo  
**Next Action:** Enhance existing QuantumVaR and Chemistry examples
