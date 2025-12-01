---
layout: default
title: Subset Selection Example
---

# Subset Selection Framework

For complete, working examples of solving subset selection problems (Knapsack, Portfolio Optimization, Subset Sum), see:

## 📂 Working Examples

### [Investment Portfolio Example](../../examples/InvestmentPortfolio/)
Portfolio optimization with budget constraints
- ✅ Multi-objective optimization (return vs. risk)
- ✅ Budget and diversification constraints
- ✅ Real-world investment selection
- ✅ Quantum-inspired optimization

### [Knapsack Example](../../examples/Knapsack/)
0/1 Knapsack problem solving
- ✅ Maximize value within weight limit
- ✅ Classical and quantum solvers
- ✅ Multiple constraint dimensions

### [Supply Chain Example](../../examples/SupplyChain/)
Resource allocation and subset selection
- ✅ Multi-constraint optimization
- ✅ Cost minimization
- ✅ Supply-demand balancing

**Quick Start:**

```bash
# Portfolio Optimization
cd examples/InvestmentPortfolio
dotnet fsi InvestmentPortfolio.fsx

# Knapsack Problem
cd examples/Knapsack
dotnet fsi Knapsack.fsx
```

## Problem Types Covered

| Problem | Example Folder | Description |
|---------|----------------|-------------|
| **Portfolio Optimization** | InvestmentPortfolio | Select investments within budget |
| **0/1 Knapsack** | Knapsack | Maximize value within weight limit |
| **Resource Allocation** | SupplyChain | Optimize resource distribution |
| **Subset Sum** | Knapsack | Find items summing to target |

## Documentation

For API documentation and guides:
- [Getting Started Guide](../getting-started.md) - Quick start and basic concepts
- [API Reference](../api-reference.md) - Complete API documentation
- [QUBO Encoding Strategies](../qubo-encoding-strategies.md) - How problems are encoded
