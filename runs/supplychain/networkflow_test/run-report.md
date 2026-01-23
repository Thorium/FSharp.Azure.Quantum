# Supply Chain Network Flow Optimization

This example models supply chain planning as **route activation** (binary decision per route), and compares:

- Classical baseline: greedy route activation
- Quantum: QAOA via `QuantumNetworkFlowSolver` (LocalBackend)

Important: this is *not* a continuous min-cost flow model. It is a small, backend-friendly formulation that is useful as a template for building stronger encodings.

## Inputs

- Nodes: `examples/SupplyChain/_data/nodes_tiny.csv` (sha256: `f2e3b0fa3538c2b92c4490e43151089e189355474dc62a03f0a01ae72abfee7c`)
- Routes: `examples/SupplyChain/_data/routes_tiny.csv` (sha256: `92e870004ad55fc88db553c29dabf93663c5868623ee2b1fe3f2ef5271d6de2b`)

## Outputs

- `solution_classical.csv`
- `solution_quantum.csv`
- `violations.csv`
- `metrics.json`
