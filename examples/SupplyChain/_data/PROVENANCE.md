# Tiny SupplyChain Example Data - Provenance

This folder contains a small, hand-curated supply chain network intended for running the examples out-of-the-box.

- The data is synthetic.
- Node and route names are illustrative only.
- Route costs in `routes_tiny.csv` are chosen so that the classical greedy baseline and the optimum differ (changed 2026-10-01; before that greedy happened to find the optimum, so the two could only tie): warehouse W2 has the cheapest route into customer C2 (7 against 8 from W1) but the dearest feeds (14 and 16), so taking the cheapest route into each customer costs 31, while routing both customers through W1 costs 23, the unique optimum among all 255 route sets.
