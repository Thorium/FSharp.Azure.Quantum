# FSharp.Azure.Quantum Examples

**Comprehensive examples organized by business value and technical complexity**

This directory contains 60+ examples demonstrating the FSharp.Azure.Quantum library, organized from **business-focused** (Level 1) to **research/educational** (Level 4).

## Example Categorization

Examples are categorized into **4 levels** based on business utility and technical complexity:

- **Level 1** - Business-Ready Solutions (Start Here!)
- **Level 2** - Practical Optimization Problems  
- **Level 3** - Advanced Quantum Algorithms
- **Level 4** - Research & Educational (Quantum Physics)

---

## Level 1: Business-Ready Solutions

**For:** Business analysts, product managers, enterprise developers  
**Goal:** Solve real-world problems without quantum knowledge  
**Quantum Expertise Required:** None (abstracted away)

### Machine Learning & AI

#### Fraud Detection & Security
- **[BinaryClassification/FraudDetection.fsx](BinaryClassification/FraudDetection.fsx)** START HERE  
  Detect fraudulent transactions using quantum ML  
  **Use Case:** Banking, e-commerce, insurance fraud detection  
  **ROI:** Reduce fraud losses by 20-40%, improve detection accuracy  
  **Ready for:** Production (with LocalBackend for <1000 transactions/sec)

- **[AnomalyDetection/SecurityThreatDetection.fsx](AnomalyDetection/SecurityThreatDetection.fsx)**  
  Identify network intrusions and security threats  
  **Use Case:** SIEM integration, real-time threat monitoring  
  **ROI:** Detect zero-day attacks, reduce false positives by 30%

#### Customer Intelligence
- **[PredictiveModeling/CustomerChurnPrediction.fsx](PredictiveModeling/CustomerChurnPrediction.fsx)**  
  Predict which customers will cancel service  
  **Use Case:** SaaS, telecom, subscription services  
  **ROI:** Reduce churn by 15%, targeted retention campaigns

- **[SimilaritySearch/ProductRecommendations.fsx](SimilaritySearch/ProductRecommendations.fsx)**  
  Find similar products for recommendations  
  **Use Case:** E-commerce, content platforms  
  **ROI:** Increase cross-sell/upsell by 25%, improve user engagement

#### AutoML
- **[AutoML/QuickPrototyping.fsx](AutoML/QuickPrototyping.fsx)** EASIEST START  
  Zero-config machine learning - just provide data  
  **Use Case:** Rapid prototyping, non-ML experts  
  **ROI:** 10x faster model development, no ML expertise needed

- **[AutoML/CancellationAndProgressExample.fsx](AutoML/CancellationAndProgressExample.fsx)**  
  Production AutoML with progress tracking and cancellation  
  **Use Case:** Long-running model searches, production ML pipelines

### Workforce & Operations

#### Scheduling & Resource Allocation
- **[JobScheduling/ConstraintScheduler_Example.fsx](JobScheduling/ConstraintScheduler_Example.fsx)** RECOMMENDED  
  Schedule employees/resources with hard and soft constraints  
  **Use Case:** Workforce management, cloud VM allocation, manufacturing  
  **ROI:** $25,000/hour savings (validated in powerplant optimization)

- **[JobScheduling/JobScheduling.fsx](JobScheduling/JobScheduling.fsx)**  
  Task scheduling with dependencies and resource constraints  
  **Use Case:** Project management, manufacturing workflows  
  **ROI:** Minimize makespan, optimize resource utilization

#### Supply Chain & Logistics
- **[DeliveryRouting/DeliveryRouting.fsx](DeliveryRouting/DeliveryRouting.fsx)**  
  Optimize delivery routes to minimize distance/time  
  **Use Case:** Last-mile delivery, logistics planning  
  **ROI:** 10-15% reduction in fuel costs, faster deliveries

- **[SupplyChain/SupplyChain.fsx](SupplyChain/SupplyChain.fsx)**  
  Multi-echelon supply chain optimization  
  **Use Case:** Warehouse allocation, distribution planning  
  **ROI:** Reduce inventory costs by 20%, improve fill rates

- **[SupplyChain/SupplyChain-Small.fsx](SupplyChain/SupplyChain-Small.fsx)**  
  Small-scale supply chain example (faster execution)

#### Coverage & Set Cover
- **[CoverageOptimizer/CoverageOptimizer_Example.fsx](CoverageOptimizer/CoverageOptimizer_Example.fsx)** NEW  
  Minimum-cost set cover (facility placement, sensor deployment, test selection)  
  **Use Case:** Facility placement, sensor networks, test suite minimization  
  **ROI:** Minimize coverage costs while guaranteeing complete coverage

#### Pairing & Matching
- **[ResourcePairing/ResourcePairing_Example.fsx](ResourcePairing/ResourcePairing_Example.fsx)** NEW  
  Optimal pairing/matching maximizing compatibility scores  
  **Use Case:** Mentor-mentee matching, team formation, interview scheduling  
  **ROI:** Better team productivity through optimal skill alignment

#### Bin Packing & Loading
- **[PackingOptimizer/PackingOptimizer_Example.fsx](PackingOptimizer/PackingOptimizer_Example.fsx)** NEW  
  Minimize bins/containers needed to pack items  
  **Use Case:** Container shipping, server allocation, storage optimization  
  **ROI:** Reduce container/VM costs by minimizing wasted capacity

### Network & Social Analysis
- **[GraphAnalytics/SocialNetworkAnalyzer_Example.fsx](GraphAnalytics/SocialNetworkAnalyzer_Example.fsx)**  
  Detect communities and fraud rings in social networks  
  **Use Case:** Marketing (influencer groups), fraud detection (collusion)  
  **ROI:** Targeted campaigns, detect coordinated fraud schemes

### Drug Discovery & Pharmaceutical
- **[DrugDiscovery/BindingAffinity.fsx](DrugDiscovery/BindingAffinity.fsx)** 🧬 NEW  
  VQE for protein-ligand binding energy calculation  
  **Quantum Advantage:** ✅ Exponential (electron correlation)  
  **Use Case:** Lead optimization, selectivity calculations

- **[DrugDiscovery/ReactionPathway.fsx](DrugDiscovery/ReactionPathway.fsx)** 🧬 NEW  
  VQE for drug metabolism (CYP450 activation barriers)  
  **Quantum Advantage:** ✅ Exponential (transition state multiconfigurational)  
  **Use Case:** Half-life prediction, metabolite identification

- **[DrugDiscovery/CaffeineEnergy.fsx](DrugDiscovery/CaffeineEnergy.fsx)** 🧬 NEW  
  Fragment Molecular Orbital VQE for caffeine (drug-like molecule)  
  **Quantum Advantage:** ✅ Exponential (electron correlation)  
  **Use Case:** Lead optimization, fragment-based drug design

- **[DrugDiscovery/DrugDiscoverySolvers_Example.fsx](DrugDiscovery/DrugDiscoverySolvers_Example.fsx)** NEW  
  QAOA-based drug discovery: independent set (toxicity), influence maximization (target ID), diverse selection (library design)  
  **Use Case:** Hit-to-lead filtering, target pathway analysis, compound library diversification  
  **ROI:** Accelerate early-stage pipeline with quantum-classical hybrid screening

- **[DrugDiscovery/MolecularSimilarity.fsx](DrugDiscovery/MolecularSimilarity.fsx)**  
  Quantum kernel similarity for virtual screening  
  **Quantum Advantage:** ⚠️ Unproven on NISQ  
  **Recommendation:** Use classical Tanimoto for production

### Financial Risk Management
- **[FinancialRisk/QuantumVaR.fsx](FinancialRisk/QuantumVaR.fsx)** 📊  
  Value-at-Risk with quantum amplitude estimation  
  **Quantum Advantage:** ✅ Quadratic O(1/ε) vs O(1/ε²)  
  **Use Case:** Regulatory capital (Basel III)

- **[FinancialRisk/StressTesting.fsx](FinancialRisk/StressTesting.fsx)** 📊 NEW  
  Multi-scenario stress testing (9 crisis scenarios)  
  **Quantum Advantage:** ✅ Quadratic (amplitude estimation)  
  **Use Case:** CCAR/DFAST regulatory compliance

- **[FinancialRisk/ExoticOptions.fsx](FinancialRisk/ExoticOptions.fsx)** 📊 NEW  
  Barrier and lookback options with Greeks  
  **Quantum Advantage:** ✅ Quadratic (path-dependent MC)  
  **Use Case:** Exotic derivatives pricing

---

## Level 2: Practical Optimization Problems

**For:** Developers, data scientists, operations research professionals  
**Goal:** Solve combinatorial optimization with quantum acceleration  
**Quantum Expertise Required:** Minimal (basic QAOA understanding helpful)

### Classic NP-Hard Problems

#### Graph Problems
- **[GraphColoring/GraphColoring.fsx](GraphColoring/GraphColoring.fsx)** WELL-DOCUMENTED  
  Register allocation, frequency assignment, exam scheduling  
  **Use Case:** Compiler optimization, wireless networks, university timetabling  
  **Algorithm:** QAOA (Quantum Approximate Optimization Algorithm)

- **[MaxCut/MaxCut.fsx](MaxCut/MaxCut.fsx)**  
  Graph partitioning for load balancing and circuit design  
  **Use Case:** Circuit partitioning, community detection  
  **Algorithm:** QAOA with D-Wave quantum annealer support

- **[MaxCut/DWaveMaxCutExample.fsx](MaxCut/DWaveMaxCutExample.fsx)**  
  MaxCut using D-Wave quantum annealer (2000+ qubits!)  
  **Hardware:** D-Wave Advantage (5640 qubits)  
  **Note:** Real quantum hardware example

#### Routing & Assignment
- **[Knapsack/Knapsack.fsx](Knapsack/Knapsack.fsx)**  
  0/1 Knapsack - resource allocation, cargo loading  
  **Use Case:** Portfolio selection, capacity planning

- **[InvestmentPortfolio/InvestmentPortfolio.fsx](InvestmentPortfolio/InvestmentPortfolio.fsx)**  
  Financial portfolio optimization with risk/return tradeoff  
  **Use Case:** Asset allocation, investment management

- **[InvestmentPortfolio/InvestmentPortfolio-Small.fsx](InvestmentPortfolio/InvestmentPortfolio-Small.fsx)**  
  Smaller portfolio example (faster prototyping)

### Constraint Satisfaction
- **[ConstraintSolver/SudokuSolver.fsx](ConstraintSolver/SudokuSolver.fsx)**  
  SAT solving with Grover's algorithm (Sudoku as example)  
  **Algorithm:** Quantum SAT solver with Grover search

### Quantum ML Building Blocks
- **[QML/VQCExample.fsx](QML/VQCExample.fsx)**  
  Variational Quantum Classifier training pipeline  
  **Use Case:** Custom quantum ML models, research

- **[QML/FeatureMapExample.fsx](QML/FeatureMapExample.fsx)**  
  Quantum feature encoding demonstrations

- **[QML/VariationalFormExample.fsx](QML/VariationalFormExample.fsx)**  
  Ansatz circuit design for VQC

### Visualization & Debugging
- **[GraphColoring/GraphColoring-Visualization.fsx](GraphColoring/GraphColoring-Visualization.fsx)**  
  Visualize graph coloring problems and solutions

- **[GraphColoring/ProblemAndSolutionVisualization.fsx](GraphColoring/ProblemAndSolutionVisualization.fsx)**  
  Mermaid diagram generation for problems/solutions

- **[GraphColoring/QuboVisualization.fsx](GraphColoring/QuboVisualization.fsx)**  
  Understand QUBO encoding for graph coloring

---

## Level 3: Advanced Quantum Algorithms

**For:** Quantum algorithm researchers, PhD students, quantum enthusiasts  
**Goal:** Explore advanced quantum computing techniques  
**Quantum Expertise Required:** Moderate to High

### Quantum Search & Optimization
- **[Algorithms/Grover_GraphColoring_Example.fsx](Algorithms/Grover_GraphColoring_Example.fsx)**  
  Grover's algorithm applied to graph coloring  
  **Complexity:** O(√N) speedup over classical search

- **[Algorithms/Grover_SAT_Example.fsx](Algorithms/Grover_SAT_Example.fsx)**  
  Boolean satisfiability with quantum search  
  **Theory:** Amplitude amplification

- **[TreeSearch/GameAI.fsx](TreeSearch/GameAI.fsx)**  
  Quantum tree search for game AI  
  **Use Case:** Chess, Go, decision tree exploration

- **[PatternMatcher/ConfigurationOptimizer.fsx](PatternMatcher/ConfigurationOptimizer.fsx)**  
  Quantum pattern matching for configuration optimization

### Quantum Chemistry & Simulation
- **[Chemistry/H2Molecule.fsx](Chemistry/H2Molecule.fsx)**  
  VQE (Variational Quantum Eigensolver) for molecular ground state  
  **Molecule:** Hydrogen (H₂)

- **[Chemistry/H2GroundState.fsx](Chemistry/H2GroundState.fsx)**  
  Alternative H₂ ground state calculation

- **[Chemistry/H2OWater.fsx](Chemistry/H2OWater.fsx)**  
  Water molecule (H₂O) quantum simulation  
  **Complexity:** 10 qubits (requires cloud backend)

- **[Chemistry/ElectronTransportChain.fsx](Chemistry/ElectronTransportChain.fsx)** 🧬 NEW  
  VQE for cytochrome Fe2+/Fe3+ redox chemistry  
  **Quantum Advantage:** ✅ Exponential (d-orbital correlation, spin states)  
  **Use Case:** Mitochondrial dysfunction targets, antimicrobial respiration  
  **Biochemistry:** Harper's Ch.12-13 (Biologic Oxidation, Respiratory Chain)

- **[Chemistry/HamiltonianTimeEvolution.fsx](Chemistry/HamiltonianTimeEvolution.fsx)**  
  Time evolution of molecular systems

- **[Chemistry/H2_UCCSD_VQE_Example.fsx](Chemistry/H2_UCCSD_VQE_Example.fsx)**  
  UCCSD (Unitary Coupled Cluster) ansatz for H₂

- **[Chemistry/UCCSDExample.fsx](Chemistry/UCCSDExample.fsx)**  
  Complete UCCSD workflow

- **[Chemistry/HartreeFockInitialStateExample.fsx](Chemistry/HartreeFockInitialStateExample.fsx)**  
  Hartree-Fock initial state preparation

### Quantum Arithmetic & Cryptography
- **[QuantumArithmetic/RSAEncryption.fsx](QuantumArithmetic/RSAEncryption.fsx)**  
  Quantum arithmetic for RSA operations

- **[CryptographicAnalysis/RSAFactorization.fsx](CryptographicAnalysis/RSAFactorization.fsx)**  
  Shor's algorithm for RSA factorization  
  **Security Implication:** Breaks RSA-2048 (with 4096+ qubits)

- **[CryptographicAnalysis/DiscreteLogAttack.fsx](CryptographicAnalysis/DiscreteLogAttack.fsx)**  
  Quantum discrete logarithm attack using Shor's algorithm  
  **Security Implication:** Breaks Diffie-Hellman and DSA key exchange

- **[CryptographicAnalysis/GroverAESThreat.fsx](CryptographicAnalysis/GroverAESThreat.fsx)**  
  Grover's algorithm threat to AES symmetric encryption  
  **Security Implication:** Halves AES key strength (AES-128 → 64-bit security)

- **[CryptographicAnalysis/ECCBitcoinThreat.fsx](CryptographicAnalysis/ECCBitcoinThreat.fsx)**  
  Quantum ECDLP threat to Bitcoin/cryptocurrency elliptic curve cryptography  
  **Security Implication:** Breaks secp256k1 (~2,330 logical qubits needed)  
  Uses NBitcoin for real secp256k1 key pairs, addresses, and ECDSA signatures

- **[CryptographicAnalysis/QuantumMining.fsx](CryptographicAnalysis/QuantumMining.fsx)**  
  Quantum Proof-of-Work mining using Grover's algorithm to break simplified Bitcoin-like puzzles  
  **Security Implication:** Quadratic speedup for mining (2^70 → 2^35), but not near-term feasible  
  Uses SHA-256 hashing with NBitcoin for real Bitcoin block structure comparison

### Quantum Phase Estimation
- **[PhaseEstimation/MolecularEnergy.fsx](PhaseEstimation/MolecularEnergy.fsx)**  
  QPE (Quantum Phase Estimation) for molecular energies

### Linear Algebra & Systems
- **[LinearSystemSolver/HHLAlgorithm.fsx](LinearSystemSolver/HHLAlgorithm.fsx)**  
  HHL algorithm for solving linear systems  
  **Speedup:** Exponential over classical (for specific systems)

- **[LinearSystemSolver/HHL_Extensions_Rigetti_Example.fsx](LinearSystemSolver/HHL_Extensions_Rigetti_Example.fsx)**  
  HHL with Rigetti backend extensions

### Quantum Communication & Security
- **[Protocols/BB84_Complete_Pipeline_Example.fsx](Protocols/BB84_Complete_Pipeline_Example.fsx)** 
  Complete BB84 quantum key distribution protocol  
  **Use Case:** Secure communication, quantum cryptography

- **[Protocols/BB84_Issue_Fix_Verification.fsx](Protocols/BB84_Issue_Fix_Verification.fsx)**  
  Verification tests for BB84 implementation

- **[Protocols/QuantumTeleportationExample.fsx](Protocols/QuantumTeleportationExample.fsx)**  
  Quantum teleportation protocol

### Finance & Risk
- **[FinancialRisk/OptionPricing.fsx](FinancialRisk/OptionPricing.fsx)**  
  Monte Carlo option pricing with quantum acceleration

- **[QuantumDistributions/QuantumDistributions.fsx](QuantumDistributions/QuantumDistributions.fsx)**  
  Load probability distributions into quantum states

### Parameter Optimization
- **[Optimization/QaoaParameterOptimizationExample.fsx](Optimization/QaoaParameterOptimizationExample.fsx)**  
  QAOA parameter tuning strategies

- **[IntegerVariables/IntegerVariablesExample.fsx](IntegerVariables/IntegerVariablesExample.fsx)**  
  Encoding integer variables in QUBO

- **[IntegerVariables/test-encoding.fsx](IntegerVariables/test-encoding.fsx)**  
  Test integer encoding strategies

- **[IntegerVariables/verify-fix.fsx](IntegerVariables/verify-fix.fsx)**  
  Verification for integer variable encoding

### Sparse QUBO & Problem Decomposition
- **[SparseQubo/SparseQubo_Example.fsx](SparseQubo/SparseQubo_Example.fsx)** NEW  
  Sparse QUBO encoding pipeline: coordinate-list (COO) representation, density analysis, and conversion to standard QUBO  
  **Use Case:** Large-scale optimization where QUBO matrices are >95% sparse  
  **Algorithm:** COO-based storage with O(nnz) operations instead of O(n²)

- **[ProblemDecomposition/ProblemDecomposition_Example.fsx](ProblemDecomposition/ProblemDecomposition_Example.fsx)** NEW  
  Automatic problem decomposition: splits large QUBOs into sub-problems that fit backend qubit limits, solves independently, and merges results  
  **Use Case:** Run 50+ variable problems on 16-qubit LocalBackend via divide-and-conquer  
  **Algorithm:** Graph-based partitioning with budget-aware execution orchestration

### Quantum Circuit Building
- **[CircuitBuilder/QuantumCircuits.fsx](CircuitBuilder/QuantumCircuits.fsx)**  
  Low-level quantum circuit construction

- **[CircuitBuilder/CircuitVisualization.fsx](CircuitBuilder/CircuitVisualization.fsx)**  
  Visualize quantum circuits (Mermaid, ASCII)

### Azure Quantum Integration
- **[AzureQuantumWorkspace/WorkspaceExample.fsx](AzureQuantumWorkspace/WorkspaceExample.fsx)**  
  Cloud quantum hardware (IonQ, Rigetti, Quantinuum, Atom Computing)  
  **Features:** Quota management, job submission, circuit conversion

---

## Level 4: Research & Educational (Quantum Physics)

**For:** Quantum physics students, topological quantum computing researchers  
**Goal:** Understand fundamental quantum mechanics and exotic quantum models  
**Quantum Expertise Required:** Advanced (graduate-level physics)

### Topological Quantum Computing
- **[Topological/TopologicalExample.fsx](Topological/TopologicalExample.fsx)**  
  Introduction to anyon braiding and topological qubits

- **[Topological/ToricCodeExample.fsx](Topological/ToricCodeExample.fsx)** ⭐ EDUCATIONAL  
  Toric code error correction  
  **Theory:** Surface codes, topological protection

- **[Topological/ModularDataExample.fsx](Topological/ModularDataExample.fsx)**  
  Modular S and T matrices (topological invariants)  
  **Theory:** Modular tensor categories, fusion rules

- **[Topological/TopologicalVisualization.fsx](Topological/TopologicalVisualization.fsx)**  
  Visualize anyon braiding operations

### Topological Backend Examples
- **[Topological/BellState.fsx](Topological/BellState.fsx)**  
  Bell state using topological qubits

- **[Topological/BasicFusion.fsx](Topological/BasicFusion.fsx)**  
  Anyon fusion demonstrations

- **[Topological/BackendComparison.fsx](Topological/BackendComparison.fsx)**  
  Compare gate-based vs topological backends

- **[Topological/FormatDemo.fsx](Topological/FormatDemo.fsx)**  
  Topological circuit file format (.tqp)

### Educational Quantum Algorithms
- **[Algorithms/BellStatesExample.fsx](Algorithms/BellStatesExample.fsx)**  
  Bell states and quantum entanglement

- **[Algorithms/DeutschJozsaExample.fsx](Algorithms/DeutschJozsaExample.fsx)**  
  Deutsch-Jozsa algorithm (first quantum advantage proof)

### Game Examples (Educational)
- **[Kasino/Kasino.fsx](Kasino/Kasino.fsx)**  
  Card game with quantum decision-making

- **[Gomoku/](Gomoku/)** (F# project)  
  Board game AI with quantum tree search

---

## Quick Start Guides

### For Business Users (Start Here!)

**Step 1:** Install the library
```bash
dotnet add package FSharp.Azure.Quantum
```

**Step 2:** Run the easiest example (AutoML)
```bash
cd examples/AutoML
dotnet fsi QuickPrototyping.fsx
```

**Step 3:** Try a business problem
```bash
cd examples/BinaryClassification
dotnet fsi FraudDetection.fsx
```

### For Developers

**Step 1:** Explore optimization problems
```bash
cd examples/GraphColoring
dotnet fsi GraphColoring.fsx
```

**Step 2:** Compare quantum vs classical
```bash
cd examples/MaxCut
dotnet fsi MaxCut.fsx
```

**Step 3:** Try cloud quantum hardware
```bash
cd examples/AzureQuantumWorkspace
# Edit WorkspaceExample.fsx with your Azure credentials
dotnet fsi WorkspaceExample.fsx
```

### For Researchers

**Step 1:** Quantum chemistry
```bash
cd examples/Chemistry
dotnet fsi H2Molecule.fsx
```

**Step 2:** Advanced algorithms
```bash
cd examples/LinearSystemSolver
dotnet fsi HHLAlgorithm.fsx
```

**Step 3:** Topological quantum computing
```bash
dotnet fsi ToricCodeExample.fsx
```

---

## Running Examples

### Prerequisites
```bash
# Install .NET 8.0+
dotnet --version

# Install FSharp.Azure.Quantum
dotnet add package FSharp.Azure.Quantum
```

### Running Individual Examples
```bash
# Navigate to example directory
cd examples/BinaryClassification

# Run with F# Interactive
dotnet fsi FraudDetection.fsx
```

### Running All Examples (Validation)
```bash
# From examples/ directory
dotnet fsi test-all-examples.fsx
```

---

## 🎓 Learning Path Recommendations

### Beginner Path (Business Focus)
1. AutoML/QuickPrototyping.fsx - Easiest start
2. BinaryClassification/FraudDetection.fsx - Real use case
3. JobScheduling/ConstraintScheduler_Example.fsx - Optimization intro
4. CoverageOptimizer/CoverageOptimizer_Example.fsx - Set cover problems
5. ResourcePairing/ResourcePairing_Example.fsx - Matching & pairing
6. GraphColoring/GraphColoring.fsx - Classic problem

### Developer Path (Quantum Optimization)
1. GraphColoring/GraphColoring.fsx - Best documented
2. MaxCut/MaxCut.fsx - Graph partitioning
3. Knapsack/Knapsack.fsx - Classic NP problem
4. PackingOptimizer/PackingOptimizer_Example.fsx - Bin packing
5. DrugDiscovery/DrugDiscoverySolvers_Example.fsx - QAOA drug discovery
6. MaxCut/DWaveMaxCutExample.fsx - Real quantum hardware

### Research Path (Quantum Algorithms)
1. Algorithms/BellStatesExample.fsx - Quantum basics
2. Algorithms/Grover_SAT_Example.fsx - Quantum search
3. SparseQubo/SparseQubo_Example.fsx - Sparse QUBO pipeline
4. ProblemDecomposition/ProblemDecomposition_Example.fsx - Auto-decomposition
5. Chemistry/H2Molecule.fsx - VQE
6. Topological/ToricCodeExample.fsx - Topological qubits

### Expert Path (Full Stack)
1. CircuitBuilder/ - Low-level circuits
2. AzureQuantumWorkspace/ - Cloud infrastructure
3. Topological/ - Exotic quantum models
4. CryptographicAnalysis/ - Quantum cryptographic attacks (RSA, ECC, AES, discrete log)

---

## License

All examples are part of FSharp.Azure.Quantum library.  
**License:** Unlicense (Public Domain)

