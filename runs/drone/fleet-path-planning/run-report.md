# Drone Fleet Path Planning Results

## Summary

- **Run ID**: 20260123_222746
- **Method**: Hybrid → Classical
- **Waypoints**: 8
- **Total Distance**: 15.56 km
- **Estimated Flight Time**: 21.6 min
- **Energy Consumption**: 500.0 Wh
- **Elapsed Time**: 79 ms

## Optimized Route

| # | Waypoint | Name | Latitude | Longitude | Altitude (m) |
|---|----------|------|----------|-----------|--------------|
| 1 | WP001 | Base Station | 37.7749 | -122.4194 | 50 |
| 2 | WP008 | Charging Station | 37.7649 | -122.4094 | 50 |
| 3 | WP005 | Inspection Site 2 | 37.7549 | -122.3994 | 90 |
| 4 | WP003 | Delivery Point B | 37.7649 | -122.4294 | 80 |
| 5 | WP006 | Emergency Zone | 37.7749 | -122.4494 | 60 |
| 6 | WP007 | Relay Point | 37.7849 | -122.4594 | 110 |
| 7 | WP004 | Inspection Site 1 | 37.7949 | -122.4394 | 120 |
| 8 | WP002 | Delivery Point A | 37.7849 | -122.4094 | 100 |

## Quantum Computing Context

This example demonstrates mapping drone path planning to the **Traveling Salesman Problem (TSP)**:

- **Classical approach**: Nearest Neighbor heuristic + 2-opt local search
- **Quantum approach**: QAOA (Quantum Approximate Optimization Algorithm)

The **HybridSolver** automatically selects:
- Classical for small instances (<50 waypoints) - fast, free
- Quantum for large instances (>100 waypoints) - potential speedup

## Files Generated

- `metrics.json` - Performance metrics
- `optimized_route.csv` - Waypoint visitation order
