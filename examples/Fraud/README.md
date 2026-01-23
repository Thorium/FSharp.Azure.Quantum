# Fraud Examples

## Transaction Fraud Batch Scoring (MVP-style)

This example demonstrates a production-shaped flow:

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
