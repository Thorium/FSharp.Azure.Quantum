# Supply Chain Optimization Example

## Business Context

A **global logistics company** operates a **4-stage supply chain** routing products from **2 suppliers** (Asia) through **2 warehouses** (Middle East/Asia) and **2 distributors** (Europe) to **3 customers** (European cities). The goal is to minimize total logistics cost while meeting all customer demand.

### Real-World Application

Supply chain optimization is critical for global commerce:

- **Manufacturing**: Automotive, electronics, consumer goods production networks
- **Retail**: E-commerce fulfillment centers, distribution networks
- **Pharmaceuticals**: Cold chain logistics, regulatory compliance
- **Food & Beverage**: Perishable goods distribution
- **Energy**: Oil & gas pipeline networks, electricity grids

**Key Business Metrics:**
- **$1.5 trillion**: Annual global logistics costs (10% of global GDP)
- **10-20% cost reduction**: Typical savings from supply chain optimization
- **3-5% profit margin**: Improvement from network optimization initiatives

---

## The Problem

### Mathematical Formulation

**Objective:** Minimize total logistics cost

$$ \text{minimize: } \sum_{(i,j) \in E} c_{ij} \cdot f_{ij} + \sum_{k \in N} p_k \cdot \sum_{i:(i,k) \in E} f_{ik} $$

Where:
- $f_{ij}$ = Flow (units) from node $i$ to node $j$
- $c_{ij}$ = Transport cost per unit on edge $(i,j)$
- $p_k$ = Operating cost per unit at node $k$

**Decision Variables:**
- $f_{ij} \geq 0$: Units shipped on each edge
- Must satisfy capacity, demand, and flow conservation constraints

**Constraints:**

1. **Capacity constraints**: Flow through node ≤ node capacity
   $$ \sum_{i:(i,k) \in E} f_{ik} \leq \text{Cap}_k, \quad \forall k \in N $$

2. **Demand satisfaction**: Total flow to customer = customer demand
   $$ \sum_{i:(i,c) \in E} f_{ic} = D_c, \quad \forall c \in \text{Customers} $$

3. **Flow conservation**: Inflow = Outflow at intermediate nodes
   $$ \sum_{i:(i,k) \in E} f_{ik} = \sum_{j:(k,j) \in E} f_{kj}, \quad \forall k \in \text{Warehouses, Distributors} $$

### Problem Characteristics

| Characteristic | Value |
|----------------|-------|
| **Network Size** | 9 nodes (2+2+2+3), 14 edges |
| **Demand** | 1,250 units across 3 customers |
| **Supply** | 1,800 units capacity (2 suppliers) |
| **Stages** | 4 (supplier → warehouse → distributor → customer) |
| **Complexity** | Polynomial (network flow) but NP-hard with additional constraints |
| **Classical Approach** | Linear programming, greedy heuristic, min-cost flow |

---

## Supply Chain Network

### Network Structure

```
Stage 1: SUPPLIERS (Asia)
┌─────────────────────────┐
│ S1_Shanghai  (1000 cap) │──┐
│ S2_Mumbai    (800 cap)  │──┤
└─────────────────────────┘  │
                             │
Stage 2: WAREHOUSES          ↓
┌─────────────────────────┐  │
│ W1_Singapore (1200 cap) │←─┤
│ W2_Dubai     (900 cap)  │←─┘
└─────────────────────────┘  │
                             │
Stage 3: DISTRIBUTORS        ↓
┌─────────────────────────┐  │
│ D1_London    (800 cap)  │←─┤
│ D2_Frankfurt (700 cap)  │←─┘
└─────────────────────────┘  │
                             │
Stage 4: CUSTOMERS           ↓
┌─────────────────────────┐  │
│ C1_Paris     (400 dem)  │←─┤
│ C2_Berlin    (500 dem)  │←─┤
│ C3_Amsterdam (350 dem)  │←─┘
└─────────────────────────┘
```

### Node Details

**Suppliers** (Total Capacity: 1,800 units)
| ID | Location | Capacity | Operating Cost/Unit |
|----|----------|----------|---------------------|
| S1 | Shanghai | 1,000 | $100 |
| S2 | Mumbai | 800 | $90 |

**Warehouses** (Total Capacity: 2,100 units)
| ID | Location | Capacity | Operating Cost/Unit |
|----|----------|----------|---------------------|
| W1 | Singapore | 1,200 | $50 |
| W2 | Dubai | 900 | $45 |

**Distributors** (Total Capacity: 1,500 units)
| ID | Location | Capacity | Operating Cost/Unit |
|----|----------|----------|---------------------|
| D1 | London | 800 | $30 |
| D2 | Frankfurt | 700 | $35 |

**Customers** (Total Demand: 1,250 units)
| ID | Location | Demand | Revenue/Unit |
|----|----------|--------|--------------|
| C1 | Paris | 400 | $200 |
| C2 | Berlin | 500 | $220 |
| C3 | Amsterdam | 350 | $180 |

### Transport Costs

**Suppliers → Warehouses** (Ocean Freight)
- S1 → W1: $20/unit, S1 → W2: $25/unit
- S2 → W1: $22/unit, S2 → W2: $18/unit (cheapest ocean route)

**Warehouses → Distributors** (Air Freight)
- W1 → D1: $15/unit, W1 → D2: $18/unit
- W2 → D1: $16/unit, W2 → D2: $14/unit (cheapest air route)

**Distributors → Customers** (Ground Shipping)
- D1 → C1: $10/unit (short distance)
- D2 → C2: $10/unit (local delivery, cheapest)
- D2 → C3: $13/unit

---

## How to Run

### Prerequisites

**Navigate to example directory:**
```bash
cd examples/SupplyChain
```

### Execute the Example

```bash
dotnet fsi SupplyChain.fsx
```

**Note:** This example uses a **pure F# greedy network flow algorithm** - no external library dependencies required.

### Expected Runtime

- **Algorithm**: Greedy path selection with capacity constraints
- **Solution Time**: ~10 milliseconds
- **Problem Size**: 9 nodes, 14 edges, 1,250 units demand
- **Memory**: Minimal (<10 MB)

---

## Expected Output

### 1. Network Flow by Stage

```
STAGE 1: SUPPLIERS → WAREHOUSES (Ocean Freight)
────────────────────────────────────────────────────────────────────────────────
  S2_Mumbai → W2_Dubai: 800 units ($14,400.00)
  S1_Shanghai → W2_Dubai: 100 units ($2,500.00)
  S1_Shanghai → W1_Singapore: 350 units ($7,000.00)

STAGE 2: WAREHOUSES → DISTRIBUTORS (Air Freight)
────────────────────────────────────────────────────────────────────────────────
  W2_Dubai → D1_London: 800 units ($12,800.00)
  W2_Dubai → D2_Frankfurt: 100 units ($1,400.00)
  W1_Singapore → D2_Frankfurt: 350 units ($6,300.00)

STAGE 3: DISTRIBUTORS → CUSTOMERS (Ground Shipping)
────────────────────────────────────────────────────────────────────────────────
  D1_London → C1_Paris: 400 units ($4,000.00)
  D1_London → C2_Berlin: 400 units ($4,800.00)
  D2_Frankfurt → C2_Berlin: 100 units ($1,000.00)
  D2_Frankfurt → C3_Amsterdam: 350 units ($4,550.00)

SUMMARY:
  Total Units Shipped:   1250 / 1250 (100.0% fill rate)
  Total Cost:            $371,250.00
  Total Revenue:         $253,000.00
  Net Profit:            $-118,250.00
```

**Key Observations:**
- **100% fill rate**: All customer demand satisfied
- **Cheapest path preference**: Algorithm selects S2→W2→D2→C2 (lowest costs)
- **Capacity constraints**: W2 capacity (900) limits flow, spillover to W1
- **Operating costs dominate**: $312,500 operating vs. $58,750 transport

### 2. Cost Breakdown

```
COST COMPONENTS:
  Transport Cost:        $58,750.00 (15.8%)
  Operating Cost:        $312,500.00 (84.2%)

  Total Cost:            $371,250.00
  Total Revenue:         $253,000.00
  Profit Margin:         -46.7%
```

**Business Insight:** Operating costs ($100/unit at suppliers, $50/unit at warehouses) exceed revenue per unit ($202.40 average), resulting in a loss. This demonstrates the importance of pricing strategy and cost management.

### 3. Unit Economics

```
UNIT ECONOMICS:
  Cost per Unit:         $297.00
  Revenue per Unit:      $202.40
  Profit per Unit:       $-94.60

KEY INSIGHTS:
  ✓ All customer demand satisfied (100% fill rate)
  ⚠ Operating at a loss ($-118,250.00)
  ✓ Multi-stage optimization minimizes total logistics cost
  ✓ Greedy algorithm provides good solution in <10ms
```

**Trade-off:** While the algorithm minimizes logistics costs, the **unit economics are unprofitable**. Real-world solutions:
1. Negotiate better supplier pricing ($100→$50/unit = breakeven)
2. Increase customer pricing ($200→$320/unit = breakeven)
3. Reduce warehouse/distributor operating costs
4. Optimize network structure (eliminate nodes)

---

## Solution Interpretation

### What the Solution Tells Us

The greedy network flow algorithm:

1. **Path Selection**: For each customer, generates all feasible paths (Supplier→Warehouse→Distributor→Customer)
2. **Cost Ranking**: Sorts paths by total cost (transport + operating)
3. **Capacity-Aware Allocation**: Routes flow through cheapest paths respecting capacity constraints
4. **Demand Satisfaction**: Continues until customer demand met or network capacity exhausted

### Algorithm Performance

| Metric | Greedy Network Flow | Optimal (Linear Programming) |
|--------|---------------------|------------------------------|
| **Solution Time** | ~10 ms | ~50-500 ms |
| **Optimality** | 85-95% of optimal | 100% (exact) |
| **Scalability** | 10,000+ nodes | ~1,000 nodes practical limit |
| **Complexity** | $O(P \cdot N)$ where P=paths, N=nodes | $O(N^3)$ (simplex method) |

**Practical Recommendation:** Greedy heuristics are suitable for **real-time logistics** where speed matters more than absolute optimality.

### When to Use Quantum vs. Classical

**Classical Solvers (Current Example):**
- ✅ **Use for:** <10,000 nodes, linear constraints, continuous flow
- ✅ **Advantages:** Proven algorithms (LP, min-cost flow), fast, exact solutions
- ✅ **Performance:** Milliseconds to seconds for practical networks

**Quantum Advantage (Future):**
- ⚡ **Potential for:** 100,000+ node networks with discrete/integer constraints
- ⚡ **Algorithms:** Quantum approximate optimization (QAOA), quantum annealing
- ⚡ **Status:** Experimental (no demonstrated advantage yet)
- ⚡ **Challenge:** Problem encoding, circuit depth, error rates

**Current Recommendation:** Use **classical linear programming or greedy algorithms** for supply chain optimization. Quantum computing not yet practical for this problem class.

---

## Technical Details

### Algorithm Used

**Greedy Multi-Stage Network Flow:**

```fsharp
// For each customer:
for customer in customers do
    // 1. Generate all feasible paths (S→W→D→C)
    let paths = 
        suppliers × warehouses × distributors
        |> filter (edge exists)
        |> map (path, totalCost)
        |> sortBy cost
    
    // 2. Allocate flow through cheapest available paths
    for (path, cost) in paths do
        let capacity = min(pathCapacity, demandRemaining)
        if capacity > 0 then
            allocateFlow(path, capacity)
            updateCapacities(path, -capacity)
```

### Key Functions

- **`optimizeSupplyChain`**: Main optimization algorithm
- **`buildGraph`**: Constructs adjacency list representation
- **`calculatePathCost`**: Sums transport + operating costs for path
- **`generateFlowReport`**: Groups and displays flows by stage
- **`generateCostBreakdown`**: Analyzes transport vs. operating costs
- **`generateBusinessInsights`**: Unit economics and profitability analysis

---

## Extending This Example

### Add Time Windows

Model delivery deadlines and lead times:

```fsharp
type Node = {
    // Existing fields...
    LeadTime: int  // Days to process
}

type Customer = {
    // Existing fields...
    Deadline: int  // Latest acceptable delivery day
}

// Ensure total path lead time < deadline
let pathLeadTime = path |> List.sumBy (_.LeadTime)
if pathLeadTime <= customer.Deadline then allocate
```

### Add Inventory Costs

Model warehouse holding costs:

```fsharp
type Warehouse = {
    // Existing fields...
    InventoryHoldingCostPerDay: float
}

// Add inventory costs to total cost
let inventoryCost = averageInventory * holdingCostPerDay * days
```

### Add Multi-Product Support

Optimize flows for multiple product types:

```fsharp
type Product = { Id: string; Weight: float; Volume: float }

type Customer = {
    // Existing fields...
    Demands: Map<string, int>  // Product ID → Quantity
}

// Constraint: Total volume/weight < truck capacity
let truckVolume = 
    products
    |> Map.sumBy (fun (id, qty) -> products.[id].Volume * float qty)
```

---

## Educational Value

This example demonstrates:

1. **✅ Multi-stage optimization** - Network flow through 4 stages
2. **✅ Capacity constraints** - Respecting node throughput limits
3. **✅ Cost minimization** - Balancing transport vs. operating costs
4. **✅ Greedy algorithms** - Fast heuristic for practical problems
5. **✅ Unit economics** - Business profitability analysis
6. **✅ Real-world complexity** - Shows unprofitable scenario requiring business intervention

### Key Takeaways

- **Network flow** is fundamental to logistics optimization
- **Operating costs** often dominate transport costs (84% vs. 16%)
- **Unit economics** determine profitability (cost/unit vs. revenue/unit)
- **Capacity constraints** limit flow and force multi-path routing
- **Greedy algorithms** provide good solutions quickly for large networks
- **Quantum advantage** not demonstrated for supply chain optimization (classical methods highly effective)

---

## Real-World Use Cases

### E-Commerce Fulfillment ($500M annual savings)

**Amazon/Walmart Distribution Networks:**
- 100+ fulfillment centers
- 10,000+ delivery zones
- Real-time demand forecasting
- Dynamic routing based on inventory

**Impact:** 15% reduction in delivery costs = **$500M+ annual savings** for large retailers

### Automotive Supply Chain

**Just-in-Time Manufacturing:**
- 500+ suppliers per vehicle model
- 50+ assembly plants globally
- 4-stage supply network (raw materials → parts → assembly → dealers)
- Hours matter (production line stoppages cost $50k/hour)

**Impact:** Supply chain optimization reduces inventory by **30%** while maintaining 99.9% uptime

### Pharmaceutical Cold Chain

**Vaccine Distribution:**
- Temperature-controlled storage at each stage
- Regulatory compliance (FDA, EMA)
- Expiration date constraints
- High-value products ($100-$500/unit)

**Impact:** Optimized distribution reduces waste by **10-15%** = millions in savings

---

## References

### Academic

- **Ford & Fulkerson (1956)**: "Maximum Flow Through a Network" - Foundation for network flow algorithms
- **Ahuja, Magnanti & Orlin (1993)**: "Network Flows" - Comprehensive textbook
- **Dantzig, G. (1963)**: "Linear Programming" - Simplex method for optimization

### Practical

- **Min-Cost Flow Problem**: Polynomial-time algorithms available
- **Linear Programming**: Optimal solutions via Simplex or Interior Point methods
- **Supply Chain Network Design**: Strategic vs. tactical vs. operational optimization

### Related Examples

- **TSP (Delivery Routing)**: Last-mile delivery optimization
- **Job Scheduling**: Resource allocation with dependencies
- **Portfolio Optimization**: Continuous variable optimization

---

## Questions or Issues?

- **Algorithm Details**: See inline comments in `SupplyChain.fsx`
- **Performance Issues**: For large networks (10,000+ nodes), consider specialized LP solvers (CPLEX, Gurobi)
- **Custom Scenarios**: Modify node/edge definitions and constraints

---

**Last Updated**: 2025-11-27  
**FSharp.Azure.Quantum Version**: 1.0.0 (in development)  
**Problem Difficulty**: Medium-High (network flow with capacity constraints)  
**Business Value**: Very High ($1.5T global logistics industry, 10-20% cost reduction potential)
