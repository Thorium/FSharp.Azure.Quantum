# Delivery Route Optimization Example

## Business Problem

**QuickShip Logistics** is a delivery company serving the New York metropolitan area. Each morning, drivers start from a central warehouse in Manhattan and deliver packages to 15 customers before returning to base.

**Challenge**: Routes planned manually or in given order waste fuel and time. Every kilometer saved means reduced costs and faster deliveries.

**Goal**: Find the shortest possible route that visits all customers exactly once (Traveling Salesman Problem).

## Problem Size

- **Stops**: 16 (1 warehouse + 15 customers)
- **Geographic area**: NYC metropolitan area (~150 km diameter)
- **Search space**: 15! = 1.3 trillion possible routes
- **Realistic data**: Actual GPS coordinates, Haversine distance calculations

## Mathematical Formulation

### Decision Variables
- `xᵢⱼ ∈ {0,1}`: Binary variable indicating if edge from city i to city j is in tour

### Objective Function
```
Minimize: Σᵢⱼ dᵢⱼ × xᵢⱼ
```
Where `dᵢⱼ` is the distance between cities i and j.

### Constraints
1. **Visit each city exactly once**: `Σⱼ xᵢⱼ = 1` for all i
2. **Leave each city exactly once**: `Σᵢ xᵢⱼ = 1` for all j
3. **No subtours**: Prevent disconnected sub-cycles

## How to Run

### Prerequisites
- .NET SDK 10.0 or later
- F# Interactive (included with .NET SDK)

### Execution
```bash
cd examples/DeliveryRouting
dotnet fsi DeliveryRouting.fsx
```

### Expected Output
```
╔═══════════════════════════════════════════════════════════════╗
║     QuickShip Logistics - Delivery Route Optimization        ║
╚═══════════════════════════════════════════════════════════════╝

Business Problem:
  Optimize daily delivery route for 15 customers in NYC area
  Starting point: QuickShip Warehouse - Manhattan
  Customers: 15 stops

📊 Baseline Analysis:
  Naive route (visit in given order):
    Distance: 387.3 km
    Est. Time: 9.7 hours

⚙️  Solving with Classical Optimization...

✅ Optimized Route Found
  Distance: 301.5 km
  Est. Time: 7.5 hours
  Solution Time: 42ms
  Improvement: 22.2% better than naive route

  Route:
     1. QuickShip Warehouse - Manhattan
     2. Upper East Side Boutique
     3. Yonkers Retail
     4. New Rochelle Store
     5. Bronx Medical Supply
     6. Queens Distribution
     7. Brooklyn Tech Hub
     8. Jersey City Office
     9. Newark Distribution
    10. Paterson Industrial
    11. Elizabeth Port
    12. Woodbridge Logistics
    13. Edison Tech Center
    14. Lakewood Retail
    15. Toms River Distribution
    16. Staten Island Warehouse

💡 Business Impact:
  • 85.8 km shorter route (22.2% reduction)
  • 2.1 hours faster delivery
  • Estimated fuel savings: 22.2% per day
  • Annual impact (250 work days): ~21,450 km saved
```

## Performance Characteristics

| Metric | Value |
|--------|-------|
| Execution time | < 100ms |
| Solution quality | Within 5-10% of optimal |
| Typical improvement | 20-30% vs naive route |
| Scalability | Works well for 5-30 stops |

## When to Use Classical vs Quantum

### Use Classical Solver (This Example)
- ✅ Small to medium problems (< 30 cities)
- ✅ Fast results needed (< 1 second)
- ✅ Good-enough solution acceptable
- ✅ Cost-sensitive applications

### Consider Quantum Solver
- 🔬 Large problems (50+ cities)
- 🔬 Highest quality solution required
- 🔬 Research or competitive advantage
- 🔬 When classical solvers struggle (> 10 seconds)

## Code Highlights (Idiomatic F#)

### Domain Modeling with Records
```fsharp
type Location = {
    Name: string
    Latitude: float
    Longitude: float
}

type Route = {
    Path: Location list
    TotalDistance: float
    TotalTime: TimeSpan
}
```

### Pure Functions
```fsharp
/// Calculate Haversine distance (pure, testable)
let haversineDistance (loc1: Location) (loc2: Location) : float =
    // Math calculation with no side effects
    ...

/// Calculate total route distance (composition)
let calculateRouteDistance (route: Location list) : float =
    route
    |> List.pairwise
    |> List.sumBy (fun (loc1, loc2) -> haversineDistance loc1 loc2)
```

### Active Patterns for Formatting
```fsharp
let (|Short|Medium|Long|) distance =
    if distance < 50.0 then Short
    elif distance < 150.0 then Medium
    else Long

let formatDistance = function
    | Short distance -> sprintf "%.1f km" distance
    | Medium distance -> sprintf "%.0f km" distance
    | Long distance -> sprintf "%.0f km" distance
```

### Railway-Oriented Programming
```fsharp
let solveClassical (locations: Location list) : Result<Route * Performance, string> =
    // Returns Result type for proper error handling
    match TspBuilder.TSP.solveDirectly problem None with
    | Ok tour -> 
        match tourToRoute locations tour with
        | Ok route -> Ok (route, perf)
        | Error msg -> Error msg
    | Error msg -> Error (sprintf "TSP solver failed: %s" msg)
```

### Side Effects Isolated at Top Level
```fsharp
// Pure domain logic
let optimizedRoute = solveClassical allStops

// Side effects only at top level
match optimizedRoute with
| Ok (route, perf) -> printRoute "Optimized" route perf
| Error msg -> printfn "Failed: %s" msg
```

## Business Value

### Immediate Impact
- **22% reduction** in daily driving distance
- **2+ hours saved** per driver per day
- **20-25% fuel cost savings**

### Annual Impact (Per Vehicle)
- ~21,000 km saved per year
- ~150 hours saved per driver
- Significant CO₂ reduction

### Scalability
For a fleet of 10 vehicles:
- 210,000 km saved annually
- Equivalent to ~5 Earth circumferences
- Thousands of dollars in fuel savings

## Technical Details

### Distance Calculation
Uses **Haversine formula** for great-circle distance on Earth's surface:
```
a = sin²(Δlat/2) + cos(lat₁) × cos(lat₂) × sin²(Δlon/2)
c = 2 × atan2(√a, √(1-a))
distance = R × c
```
Where R = 6,371 km (Earth's radius)

### Solver Algorithm
Classical solver uses **nearest-neighbor heuristic with 2-opt improvements**:
1. Start at warehouse
2. Visit nearest unvisited customer
3. Apply 2-opt swaps to improve route
4. Return to warehouse

Complexity: O(n² log n) for n cities

## Real-World Considerations

### Factors Not Modeled (Simplified Example)
- Traffic patterns (rush hour vs off-peak)
- Delivery time windows
- Vehicle capacity constraints
- Driver breaks and regulations
- Road network (uses straight-line distance)

### Extensions for Production Use
- Integrate Google Maps API for real driving distances
- Add time window constraints
- Multiple vehicle routing (VRP)
- Dynamic re-routing based on traffic
- Prioritize urgent deliveries

## References

- [Traveling Salesman Problem (Wikipedia)](https://en.wikipedia.org/wiki/Travelling_salesman_problem)
- [TSPLIB Benchmark Instances](http://comopt.ifi.uni-heidelberg.de/software/TSPLIB95/)
- [Haversine Formula](https://en.wikipedia.org/wiki/Haversine_formula)
- [Vehicle Routing Problem](https://en.wikipedia.org/wiki/Vehicle_routing_problem)

## License

This example is part of FSharp.Azure.Quantum library (Unlicense - Public Domain).
