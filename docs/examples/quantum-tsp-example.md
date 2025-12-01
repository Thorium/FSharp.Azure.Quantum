---
layout: default
title: Quantum TSP with QAOA Parameter Optimization
---

# Quantum TSP with QAOA Parameter Optimization

For complete, working examples of solving TSP using Quantum Approximate Optimization Algorithm (QAOA), see:

## 📂 [Delivery Route Optimization Example](../../examples/DeliveryRouting/)

This example demonstrates real-world quantum TSP solving with:
- ✅ QAOA-based quantum circuit construction
- ✅ Automatic parameter optimization (γ, β)
- ✅ Hybrid classical-quantum variational loop
- ✅ 16-city NYC delivery routing problem
- ✅ Complete working F# script

**Quick Start:**

```bash
cd examples/DeliveryRouting
dotnet fsi DeliveryRouting.fsx
```

## Related QAOA Examples

- **[GraphColoring](../../examples/GraphColoring/)** - Graph coloring with QAOA
- **[MaxCut](../../examples/MaxCut/)** - Max-Cut problem with QAOA
- **[Knapsack](../../examples/Knapsack/)** - 0/1 Knapsack with QAOA

## QAOA Documentation

For detailed QAOA guides and API documentation:
- [Local Simulation Guide](../local-simulation.md) - QAOA simulation and parameter tuning
- [Getting Started Guide](../getting-started.md) - QAOA quick start
- [Backend Switching Guide](../backend-switching.md) - Running QAOA on quantum hardware

## QAOA Algorithm Overview

**Key Concepts:**
- **Problem Encoding**: TSP → QUBO (Quadratic Unconstrained Binary Optimization)
- **Circuit Structure**: Alternating cost and mixer layers
- **Parameters**: (γ, β) tuned via Nelder-Mead simplex optimization
- **Hybrid Loop**: Quantum circuit evaluation + classical optimization
