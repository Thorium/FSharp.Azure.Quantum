# FSharp.Azure.Quantum - Real-World Examples

This directory contains **4 complete real-world example applications** demonstrating optimization problems across different domains. Each example includes business context, mathematical formulations, working code, expected output, and comprehensive documentation.

---

## 📋 Examples Overview

| Example | Problem Type | Business ROI | Runtime | Status |
|---------|--------------|--------------|---------|--------|
| **[DeliveryRouting](./DeliveryRouting/)** | TSP Optimization | $0.15/km fuel savings | ~5ms | ✅ Complete |
| **[InvestmentPortfolio](./InvestmentPortfolio/)** | Portfolio Optimization | 1.3x better Sharpe ratio | ~5ms | ✅ Complete |
| **[JobScheduling](./JobScheduling/)** | Resource Allocation | $25k/hour ROI | ~8ms | ✅ Complete |
| **[SupplyChain](./SupplyChain/)** | Network Flow | 10-20% cost reduction | ~10ms | ✅ Complete |

---

## 🚀 Quick Start

### Run All Examples

```bash
# From examples/ directory
cd DeliveryRouting && dotnet fsi DeliveryRouting.fsx && cd ..
cd InvestmentPortfolio && dotnet fsi InvestmentPortfolio.fsx && cd ..
cd JobScheduling && dotnet fsi JobScheduling.fsx && cd ..
cd SupplyChain && dotnet fsi SupplyChain.fsx && cd ..
```

### Prerequisites

**Build the library first** (required for DeliveryRouting and InvestmentPortfolio):
```bash
cd ../
dotnet build src/FSharp.Azure.Quantum/FSharp.Azure.Quantum.fsproj
cd examples/
```

**Note:** JobScheduling and SupplyChain are **standalone** - they don't require the library.

---

## 1. 🚚 Delivery Route Optimization

**[DeliveryRouting/](./DeliveryRouting/)** - Traveling Salesman Problem (TSP)

### Problem
Optimize delivery routes for **15 stops** in the NYC area to minimize total distance and fuel costs.

### Business Context
- **Domain**: Logistics, last-mile delivery
- **Use Case**: Daily delivery route planning for courier services
- **ROI**: $0.15/km fuel savings, 15-20% route reduction typical

### Technical Details
- **Algorithm**: Hybrid solver (automatic classical/quantum routing)
- **Uses Library**: `HybridSolver.solveTsp` from FSharp.Azure.Quantum
- **Problem Size**: 15 locations (NYC metropolitan area)
- **Solution Time**: ~5 milliseconds
- **Output**: Optimized tour with total distance 120.83 km

### Key Learnings
- TSP is NP-hard but greedy heuristics work well for <50 stops
- Geographic data (lat/lon) converts to distance matrices
- HybridSolver automatically routes to classical solver for problem sizes <50
- Classical solvers are highly effective for practical routing problems

---

## 2. 💰 Investment Portfolio Optimization

**[InvestmentPortfolio](./InvestmentPortfolio/)** - Mean-Variance Portfolio Optimization

### Problem
Allocate **$100,000** across **8 tech stocks** to maximize risk-adjusted returns (Sharpe ratio).

### Business Context
- **Domain**: Finance, wealth management
- **Use Case**: Client portfolio construction, 401(k) optimization
- **ROI**: 1% Sharpe ratio improvement = millions in better risk-adjusted returns

### Technical Details
- **Algorithm**: Hybrid solver (automatic classical/quantum routing)
- **Uses Library**: `HybridSolver.solvePortfolio` from FSharp.Azure.Quantum
- **Problem Size**: 8 assets (AAPL, MSFT, GOOGL, AMZN, NVDA, META, TSLA, AMD)
- **Solution Time**: ~5 milliseconds
- **Output**: Portfolio with 22% expected return, 25% risk, Sharpe ratio 0.88

### Key Learnings
- Mean-variance optimization balances return vs. risk
- Sharpe ratio measures return per unit of risk (higher is better)
- Classical solvers excel at continuous optimization problems
- Current implementation shows MSFT-only allocation (opportunity for diversification improvement)

---

## 3. ⚙️ Job Scheduling Optimization

**[JobScheduling/](./JobScheduling/)** - Resource Allocation with Dependencies

### Problem
Schedule **10 manufacturing jobs** across **3 machines** to minimize makespan (total completion time) while respecting dependencies.

### Business Context
- **Domain**: Manufacturing, project management, CI/CD pipelines
- **Use Case**: Production line scheduling, task allocation
- **ROI**: **$25,000/hour** cost of production delays in automotive manufacturing

### Technical Details
- **Algorithm**: Greedy scheduling with topological sort (pure F# implementation)
- **Uses Library**: None (standalone algorithm demonstration)
- **Problem Size**: 10 jobs with dependency graph (DAG), 3 machines
- **Solution Time**: ~8 milliseconds
- **Output**: 25-hour makespan with 45.3% average machine utilization

### Key Learnings
- Precedence constraints limit parallelism (critical path)
- Greedy heuristics provide 90-95% optimal solutions
- Trade-off between time savings (26.5%) and cost ($20k additional machine cost)
- Low utilization (45%) due to dependency constraints (not inefficiency)

---

## 4. 📦 Supply Chain Optimization

**[SupplyChain/](./SupplyChain/)** - Multi-Stage Network Flow

### Problem
Route **1,250 units** through a **4-stage global supply chain** (suppliers → warehouses → distributors → customers) to minimize total logistics cost.

### Business Context
- **Domain**: Logistics, global commerce
- **Use Case**: E-commerce fulfillment, manufacturing supply networks
- **ROI**: **10-20% cost reduction** from optimization (typical for global supply chains)

### Technical Details
- **Algorithm**: Greedy network flow with capacity constraints (pure F# implementation)
- **Uses Library**: None (standalone algorithm demonstration)
- **Problem Size**: 9 nodes (2 suppliers, 2 warehouses, 2 distributors, 3 customers), 14 edges
- **Solution Time**: ~10 milliseconds
- **Output**: 100% fill rate, $371k total cost, -$118k profit (demonstrates unprofitable scenario)

### Key Learnings
- Multi-stage optimization minimizes path costs through network
- Operating costs (84%) often dominate transport costs (16%)
- Unit economics (cost/unit vs. revenue/unit) determine profitability
- Educational: Shows unprofitable scenario requiring pricing/cost adjustments

---

## 📊 Comparison Matrix

### Problem Characteristics

| Example | Optimization Type | Problem Class | Complexity | Classical Performance |
|---------|-------------------|---------------|------------|----------------------|
| **DeliveryRouting** | Combinatorial | NP-hard (TSP) | $O(n!)$ exact | 90-95% optimal with heuristics |
| **InvestmentPortfolio** | Continuous | Polynomial (QP) | $O(n^2)$ | Exact solutions via LP/QP |
| **JobScheduling** | Discrete + Constraints | NP-hard | $O(n! \cdot m^n)$ | 85-95% optimal with greedy |
| **SupplyChain** | Network Flow | Polynomial | $O(n^3)$ with LP | Near-optimal with greedy |

### Business Impact

| Example | Industry | Annual Value | Optimization Benefit |
|---------|----------|--------------|---------------------|
| **DeliveryRouting** | Logistics | $100B (last-mile delivery) | 15-20% distance reduction |
| **InvestmentPortfolio** | Finance | $90T (global assets) | 1-3% better risk-adjusted returns |
| **JobScheduling** | Manufacturing | $13T (global mfg.) | 30-40% efficiency gains |
| **SupplyChain** | Retail/E-commerce | $1.5T (logistics costs) | 10-20% cost reduction |

---

## 🧪 When to Use Quantum vs. Classical

All 4 examples currently use **classical algorithms** - and that's the right choice! Here's why:

### ✅ Classical Solvers (Recommended)

**When to use:**
- Problem size <1000 variables
- Established algorithms available (LP, greedy, dynamic programming)
- Real-time or near-real-time solutions needed (<1 second)
- Production systems requiring deterministic, reliable results

**Advantages:**
- ⚡ Fast: Milliseconds for practical problem sizes
- 🎯 Proven: Decades of algorithm development and optimization
- 💰 Cost-effective: No specialized hardware required
- 🔒 Reliable: Deterministic results, well-understood behavior

### ⚡ Quantum Solvers (Future Potential)

**When quantum might help:**
- Problem size >10,000 variables with complex constraints
- Discrete/combinatorial problems where classical methods scale poorly
- Research applications exploring new solution methods

**Current status:**
- 🔬 **Experimental**: NISQ (Noisy Intermediate-Scale Quantum) hardware not yet competitive
- ⚠️ **Limited advantage**: No demonstrated speedup for practical optimization problems
- 💸 **High cost**: $100+ per quantum job execution
- 🎲 **Probabilistic**: Results require multiple runs and error mitigation

**Bottom line:** For the examples in this directory, **classical solvers are 100-1000x faster and more cost-effective** than current quantum hardware.

---

## 📚 Educational Value

### What You'll Learn

Each example demonstrates:

1. **Problem Modeling** - Translating real business problems into mathematical formulations
2. **Algorithm Design** - Practical heuristics and optimization techniques
3. **F# Best Practices** - Idiomatic functional programming patterns
4. **Performance Analysis** - Understanding algorithm complexity and scalability
5. **Business Context** - Real-world ROI and industry applications

### F# Patterns Demonstrated

- ✅ **Record types** for domain modeling
- ✅ **Pure functions** with explicit parameters (no global state)
- ✅ **Result types** for error handling
- ✅ **Pattern matching** for control flow
- ✅ **Pipeline operators** (`|>`) for data transformations
- ✅ **List comprehensions** and higher-order functions
- ✅ **Separation of concerns** (pure functions vs. I/O)

### Code Quality Standards

All examples follow **FSharp.Azure.Quantum** library conventions:
- 📝 Comprehensive inline documentation
- 🧪 Demonstrable results (included expected output)
- 🎯 Clear separation of domain model, algorithm, and reporting
- 💡 Educational comments explaining key concepts
- ⚡ Performant implementations (<100ms execution)

---

## 🔧 Directory Structure

```
examples/
├── README.md (this file)
├── DeliveryRouting/
│   ├── DeliveryRouting.fsx
│   ├── README.md
│   ├── data/
│   │   └── ny_addresses.csv
│   └── output/
│       └── expected_output.txt
├── InvestmentPortfolio/
│   ├── InvestmentPortfolio.fsx
│   ├── README.md
│   └── output/
│       └── expected_output.txt
├── JobScheduling/
│   ├── JobScheduling.fsx
│   ├── README.md
│   └── output/
│       └── expected_output.txt
└── SupplyChain/
    ├── SupplyChain.fsx
    ├── README.md
    └── output/
        └── expected_output.txt
```

Each example directory contains:
- **`*.fsx`**: Runnable F# script (self-contained)
- **`README.md`**: Comprehensive documentation (15-20 pages)
- **`output/expected_output.txt`**: Sample execution results
- **`data/`** (optional): Input data files

---

## 🎯 Target Audience

### For Developers
- Learn optimization problem modeling and algorithms
- See F# functional programming patterns in action
- Understand when to use different solution approaches

### For Data Scientists
- Practical examples of classical optimization techniques
- Business context for algorithm selection
- Performance benchmarking and complexity analysis

### For Business Stakeholders
- Concrete ROI examples with real-world metrics
- Clear problem descriptions and solution interpretations
- Cost/benefit analysis for optimization initiatives

---

## 🚀 Next Steps

### Extend the Examples

1. **Add real-time data sources** (stock APIs, GPS coordinates, etc.)
2. **Implement additional constraints** (time windows, resource limits)
3. **Compare multiple algorithms** (greedy vs. exact vs. metaheuristic)
4. **Add visualization** (route maps, Gantt charts, network graphs)

### Explore the Library

The examples use **FSharp.Azure.Quantum** library features:
- Browse `../src/FSharp.Azure.Quantum/` for full API
- See `../docs/` for detailed documentation
- Check `../tests/` for unit tests and usage patterns

### Build Your Own

Use these examples as templates:
1. Choose a problem domain
2. Model it with F# record types
3. Implement a solution algorithm
4. Generate reports and analysis
5. Document with business context

---

## 📖 References

### Academic
- **Traveling Salesman Problem**: Applegate et al., "The Traveling Salesman Problem" (2007)
- **Portfolio Optimization**: Markowitz, "Portfolio Selection" (1952) - Nobel Prize
- **Job Scheduling**: Graham, "Bounds on Multiprocessing Timing Anomalies" (1969)
- **Network Flow**: Ford & Fulkerson, "Maximum Flow Through a Network" (1956)

### Practical
- **Operations Research**: Winston & Goldberg, "Operations Research" (2003)
- **Algorithms**: Cormen et al., "Introduction to Algorithms" (CLRS)
- **F# Programming**: Petricek & Skeet, "Real-World Functional Programming" (2009)

---

## ❓ Questions & Support

### Common Issues

**Q: Example fails with "Could not load file or assembly"**  
A: Build the library first: `cd ../ && dotnet build src/FSharp.Azure.Quantum/FSharp.Azure.Quantum.fsproj`

**Q: Output differs from expected_output.txt**  
A: Minor variations are normal due to floating-point precision and algorithm randomness. Results should be within 1-5%.

**Q: How do I run on real quantum hardware?**  
A: These examples use classical algorithms. For quantum execution, see `../docs/backend-switching.md` (requires Azure Quantum credentials).

**Q: Can I use these examples in production?**  
A: Yes! The algorithms are production-quality. However, consider using specialized solvers (CPLEX, Gurobi) for mission-critical applications with >1000 variables.

### Get Help

- **Library Documentation**: `../docs/`
- **API Reference**: `../docs/api-reference.md`
- **Issue Tracker**: GitHub Issues
- **Community**: GitHub Discussions

---

**Last Updated**: 2025-11-27  
**FSharp.Azure.Quantum Version**: 1.0.0 (in development)  
**Total Examples**: 4 complete real-world applications  
**Total Documentation**: ~70 pages across all READMEs  
**Business Value**: Demonstrates $billions in global industry applications

---

## 🎉 Acknowledgments

These examples were developed as part of **TKT-50: Real-World Example Applications** to demonstrate practical optimization across diverse problem domains. Each example balances:
- ✅ **Educational value** (clear explanations and mathematical formulations)
- ✅ **Production quality** (idiomatic F#, comprehensive documentation)
- ✅ **Business relevance** (real ROI metrics and industry context)

**Happy optimizing!** 🚀
