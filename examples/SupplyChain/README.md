# Supply Chain Examples

Both examples treat supply chain planning as **route activation**: one binary decision per route, and each open route carries one unit. They choose routes, not shipment volumes.

QAOA (p = 1, the solver's default angles) samples route sets from the `QuantumNetworkFlowSolver` QUBO on the LocalBackend. A sample is a valid flow when flow is conserved at every intermediate node and no node exceeds its capacity, supply or demand. Validity has no lower bound on service, so with positive route costs a single supplier-to-customer path is the cheapest valid flow. Among the valid samples the examples therefore take the one meeting the most demand, then the cheapest. `QuantumNetworkFlowSolver.solve` in FSharp.Azure.Quantum 1.4.11 and earlier returned the cheapest valid sample, which usually serves only part of the demand; later versions rank by demand met first, as SupplyChain.fsx does.

## Multi-stage network (SupplyChain.fsx)

Suppliers, warehouses, distributors and customers: 9 nodes and 14 routes, so 14 qubits. From the repository root:

```bash
dotnet fsi examples/SupplyChain/SupplyChain.fsx
```

The script loads the NuGet package, so it samples and picks by itself, checks the picked route set against the flow rules, and compares it with an exhaustive search over all 2^14 route sets; the exhaustive optimum reaches all 3 customers at cost 139. It also prints the cheapest valid sample. In ten runs at the default 1000 shots the picked flow was always valid and reached all 3 customers, at cost 139 in three runs, 141 in two, 143 in four and 144 once. With `--shots 3000` all ten runs reached all 3 customers (cost 139 in seven, 140 in three). The cheapest valid sample reached only 1 customer, at cost 42 to 46. The demand fill rate is 0.2% (3 of 1250 units), since each route carries one unit. A sample run is in [output/expected_output.txt](output/expected_output.txt).

## Network Flow Optimization (MVP-style)

This example compares:

- Classical baseline: greedy route activation
- Quantum: `QuantumNetworkFlowSolver.solveWithShotsAsync`, built against this repository's library, which ranks valid samples by demand met, then cost

It reports two measures:

- **Customers served**: customers whose whole demand is delivered.
- **Demand fill rate**: delivered units over demanded units, one unit per open route, capped at each customer's demand.

The included tiny dataset is intentionally small to fit local simulation (8 routes, 8 qubits): two suppliers, two warehouses, two customers. Its costs set a trap for the greedy baseline. Warehouse W2 has the cheapest route into customer C2 (7, against 8 from W1) but the dearest feeds (14 and 16), so taking the cheapest route into each customer and then feeding the warehouses used costs 31. Sending both customers through W1 costs 23 (S1→W1 4, S2→W1 5, W1→C1 6, W1→C2 8). Of the 255 non-empty route sets 18 are valid flows and 10 of those serve both customers; cost 23 is the unique optimum, and the greedy flow at 31 is the next best.

### Run

From the repository root:

```bash
dotnet run --project examples/SupplyChain/NetworkFlowOptimization/NetworkFlowOptimization.fsproj -- \
  --nodes examples/SupplyChain/_data/nodes_tiny.csv \
  --routes examples/SupplyChain/_data/routes_tiny.csv \
  --out runs/supplychain/networkflow \
  --shots 100
```

A run prints both answers and writes the route sets, violations, metrics and a report to the `--out` folder:

```text
Classical greedy: cost 31, customers served 2 of 2, demand fill rate 100%, violations 0
QAOA (p = 2, optimised angles, 100 shots on Local Simulator): cost 23, customers served 2 of 2, demand fill rate 100%, violations 0
```

The solver optimises the QAOA angles for the problem on the simulator (two layers), then samples. In 150 runs at the default 100 shots QAOA returned the cost-23 optimum 147 times and the greedy flow (cost 31) 3 times. At 50 shots it found the optimum in 142 of 150 runs and at 20 shots in 85; picking 20 route sets at random would find it in about 11 runs of 150.

### Picture

![Route activation, classical greedy and QAOA](_images/supply-chain-flow.svg)

The picture puts both answers on the same network and opens their routes stage by stage, with dots for the units moving. Routes that only one of the two answers opens are drawn in brown, and each panel lists them with their costs. Both answers serve both customers. The greedy baseline pays 31: it reaches C2 through W2 (S2→W2 14, W2→C2 7). QAOA pays 23, 8 less (−26%): it reaches C2 through W1 (S2→W1 5, W1→C2 8). The headline is computed from the two answers, so a run in which QAOA only matches greedy says that they open the same routes. QAOA samples differ from run to run. Regenerate it from the repository root:

```bash
dotnet run --project examples/SupplyChain/NetworkFlowOptimization/NetworkFlowOptimization.fsproj -- --svg
```
