# Supply Chain Examples

## Network Flow Optimization (MVP-style)

This example treats supply chain planning as **route activation** (binary decision per route) and evaluates:

- Classical baseline: greedy route activation
- Quantum: QAOA via `QuantumNetworkFlowSolver` (LocalBackend by default)

The included tiny dataset is intentionally small to fit local simulation.

### Run

From `blue/git/FSharp.Azure.Quantum`:

```bash
dotnet run --project examples/SupplyChain/NetworkFlowOptimization/NetworkFlowOptimization.fsproj -- \
  --nodes examples/SupplyChain/_data/nodes_tiny.csv \
  --routes examples/SupplyChain/_data/routes_tiny.csv \
  --out runs/supplychain/networkflow \
  --shots 1000
```
