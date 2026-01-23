# Fraud Detection Examples

This folder contains quantum-enhanced fraud detection examples demonstrating different approaches to financial crime detection.

## Overview

| Example | Approach | Use Case |
|---------|----------|----------|
| `GraphFraudDetection.fsx` | QAOA graph analysis | Fraud rings, money laundering networks |
| `TransactionFraudBatchScoring/` | VQC binary classification | Transaction-level fraud scoring |

## 1. Graph-Based Fraud Detection (QAOA)

**File:** `GraphFraudDetection.fsx`

Detects fraud rings and money laundering networks using quantum graph analysis. Uses QAOA-based MaxCut for community detection in transaction networks.

### Features

- **Community Detection**: Quantum partitioning to identify suspicious clusters
- **Pattern Recognition**: Detects layering, money mules, circular transactions
- **Graph Features**: Degree, clustering coefficient, PageRank for risk scoring
- **Multi-factor Scoring**: Combines structural, behavioral, and metadata signals

### Detected Patterns

| Pattern | Description | Indicator |
|---------|-------------|-----------|
| Layering | Rapid sequential transfers | Chain of transfers < 30 min apart |
| Money Mule | Many-to-one topology | High in-degree, low out-degree |
| Circular Flow | Funds returning to origin | Cycle detection in transaction graph |
| Dense Cluster | Tightly connected accounts | High clustering coefficient |

### Run

```bash
cd examples/Fraud
dotnet fsi GraphFraudDetection.fsx
```

### Sample Output

```
Detected Fraud Patterns:
  [80% confidence] Layering: Rapid sequential transfers detected
    Involved: FRAUD04, FRAUD01, FRAUD03, FRAUD02
  [80% confidence] Money Mule: MULE01 receiving from 3 accounts
    Involved: MULE01, VICTIM01, VICTIM02, VICTIM03

High-Risk Accounts:
  FRAUD01 - Risk Score: 70.0%
    - Highly clustered connections
    - Unknown jurisdiction
    - Layering: Rapid sequential transfers detected
```

### Quantum Advantage

Graph-based fraud detection benefits from quantum computing:

1. **Community Detection**: QAOA explores 2^n partitions in superposition
2. **Quantum Walks**: Efficient graph exploration for anomaly propagation
3. **Quantum Kernels**: Capture higher-order structural patterns

### References

- Weber et al., "Anti-Money Laundering in Bitcoin" (KDD 2019)
- Negre et al., "Detecting Multiple Communities using Quantum Annealing" (PLoS ONE 2020)

---

## 2. Transaction Fraud Batch Scoring (MVP-style)

**Folder:** `TransactionFraudBatchScoring/`

Production-shaped flow for transaction-level fraud detection using Variational Quantum Classifiers.

### Features

- Read a CSV of transactions
- Train a binary classifier (quantum/hybrid)
- Evaluate on a holdout set
- Score a batch and write artifacts (`metrics.json`, `scores.csv`, PSI-style stability summary)

### Run

From `blue/git/FSharp.Azure.Quantum`:

```bash
dotnet run --project examples/Fraud/TransactionFraudBatchScoring/TransactionFraudBatchScoring.fsproj -- \
  --train examples/Fraud/_data/transactions_tiny.csv \
  --out runs/fraud/tx \
  --arch hybrid \
  --shots 1000 \
  --seed 42
```

---

## Comparison with Classical Approaches

| Aspect | Graph-Based (QAOA) | Feature-Based (VQC) |
|--------|-------------------|---------------------|
| **Input** | Transaction network | Transaction features |
| **Detects** | Fraud rings, networks | Individual fraud transactions |
| **Quantum Approach** | MaxCut community detection | Variational classification |
| **Scalability** | ~20 accounts (NISQ) | ~100s of transactions |
| **Best For** | AML, collusion, organized fraud | Card fraud, account takeover |

Both approaches can be combined: use graph analysis to identify suspicious clusters, then apply feature-based classification to individual transactions within those clusters.
