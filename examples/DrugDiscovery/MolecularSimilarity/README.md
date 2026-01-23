# Molecular Similarity (MVP-style)

Virtual screening-style similarity search using:

- Classical Tanimoto similarity over fingerprints (baseline)
- Quantum kernel similarity over molecular descriptor features (optional, local backend)

## Run

From `blue/git/FSharp.Azure.Quantum`:

```bash
dotnet run --project examples/DrugDiscovery/MolecularSimilarity/MolecularSimilarity.fsproj -- \
  --library examples/DrugDiscovery/_data/library_tiny.csv \
  --actives examples/DrugDiscovery/_data/actives_tiny.csv \
  --out runs/drugdiscovery/molsim \
  --top 5 \
  --shots 1000
```

## Outputs

The output folder contains:

- `run-config.json`
- `metrics.json`
- `run-report.md`
- `neighbors_classical.csv`
- `neighbors_quantum.csv`
- `bad_rows.csv` (if any input rows failed to parse)
