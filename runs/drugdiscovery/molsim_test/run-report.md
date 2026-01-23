# Molecular Similarity Screening

This run screens a candidate library against a set of known actives using:

- Classical baseline: fingerprint + Tanimoto similarity
- Quantum method: kernel similarity over molecular descriptors (local backend)

## Inputs

- Library: `examples/DrugDiscovery/_data/library_tiny.csv` (sha256: `ae5f9f427453c07206e7c44fd1c518c425a71ef429ad9256716a70c727c41ab4`)
- Actives: `examples/DrugDiscovery/_data/actives_tiny.csv` (sha256: `1704cccbe484a0e2eb33e0a8654a976349a307b8313bbb42ee7f6f9cc10ca9e9`)

## Parsing

- Parsed library: 10
- Parsed actives: 3

## Results

- Classical hits (avg similarity >= 0.70): 4
- Quantum hits (avg similarity >= 0.70): 1

## Outputs

- `neighbors_classical.csv` (top 3)
- `neighbors_quantum.csv` (top 3)
- `metrics.json`
