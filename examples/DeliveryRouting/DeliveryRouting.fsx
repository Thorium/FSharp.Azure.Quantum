(**
# Delivery Route Optimization

**Business Context**: 
QuickShip Logistics delivers packages to 15 customers across the New York metropolitan area daily.
Each morning, drivers start from the central warehouse in Manhattan and must visit all customers
before returning. The company wants to minimize fuel costs and delivery time.

**Problem**: 
Find the shortest route visiting all 15 customers exactly once and returning to the warehouse.
This is a Traveling Salesman Problem (TSP) - a classic combinatorial optimization problem.

**Real-World Data**: 
Actual addresses in NYC area converted to GPS coordinates. Distances calculated using
Haversine formula (great-circle distance on Earth's surface).

**Mathematical Formulation**:
- Variables: Binary xᵢⱼ (1 if edge i→j is in tour, 0 otherwise)
- Objective: Minimize Σᵢⱼ dᵢⱼ × xᵢⱼ (total distance)
- Constraints: Each city visited exactly once, no subtours

**Expected Performance**:
- Classical solver: < 100ms for 16 stops
- Quantum solver: Potential advantage for 50+ cities
- Solution quality: Within 5-10% of optimal
- Typical improvement: 20-30% better than naive route

**Quantum-Ready**: This example uses the HybridSolver which automatically routes
between classical (fast, free) and quantum (scalable) solvers based on problem size.
*)

(*
===============================================================================
 Background Theory
===============================================================================

The Traveling Salesman Problem (TSP) asks: given n cities and pairwise distances,
find the shortest tour visiting each city exactly once and returning to the start.
TSP is NP-hard, meaning no known polynomial-time algorithm exists. For n cities,
there are (n-1)!/2 possible tours; brute force is intractable beyond ~15 cities.
Classical heuristics (nearest neighbor, 2-opt, Lin-Kernighan) find good solutions
quickly, while exact methods (branch-and-bound, dynamic programming) guarantee
optimality but scale exponentially.

TSP maps to QUBO using binary variables xᵢ,ₜ ∈ {0,1} indicating city i is visited
at time t. Constraints ensure: (1) each city visited once: Σₜ xᵢ,ₜ = 1, (2) each
time has one city: Σᵢ xᵢ,ₜ = 1. The objective minimizes Σᵢⱼₜ dᵢⱼ·xᵢ,ₜ·xⱼ,ₜ₊₁.
This quadratic form suits QAOA, which explores tours in superposition. The Vehicle
Routing Problem (VRP) generalizes TSP to multiple vehicles with capacity constraints.

Key Equations:
  - Tour length: L = Σₖ₌₁ⁿ d(πₖ, πₖ₊₁) where π is a permutation of cities
  - QUBO variables: xᵢ,ₜ = 1 iff city i visited at position t
  - Row constraint: Σₜ xᵢ,ₜ = 1 for each city i
  - Column constraint: Σᵢ xᵢ,ₜ = 1 for each time t
  - Objective: Σᵢⱼₜ dᵢⱼ·xᵢ,ₜ·xⱼ,ₜ₊₁ (distance between consecutive cities)
  - Held-Karp DP: O(n²·2ⁿ) exact solution (classical baseline)

Quantum Advantage:
  TSP is a prime target for quantum optimization. QAOA can explore the tour space
  in superposition, potentially finding high-quality solutions faster than classical
  local search for large instances. Quantum annealing (D-Wave) has demonstrated
  TSP solutions for ~50 cities. The key advantage emerges for constrained variants
  (time windows, vehicle capacity, precedence) where classical methods struggle.
  Logistics companies (DHL, UPS) are exploring quantum routing; practical advantage
  requires ~1000+ qubit fault-tolerant systems.

References:
  [1] Applegate et al., "The Traveling Salesman Problem: A Computational Study",
      Princeton University Press (2006). https://doi.org/10.1515/9781400841103
  [2] Lucas, "Ising formulations of many NP problems", Front. Phys. 2, 5 (2014).
      https://doi.org/10.3389/fphy.2014.00005
  [3] Feld et al., "A Hybrid Solution Method for the Capacitated Vehicle Routing
      Problem Using a Quantum Annealer", Front. ICT 6, 13 (2019).
      https://doi.org/10.3389/fict.2019.00013
  [4] Wikipedia: Travelling_salesman_problem
      https://en.wikipedia.org/wiki/Travelling_salesman_problem
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"


open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical

// ============================================================================
// Domain Model (Idiomatic F#)
// ============================================================================

/// Geographic location with name and coordinates
type Location = {
    Name: string
    Latitude: float
    Longitude: float
}

/// Route solution with distance and path
type Route = {
    Path: Location list
    TotalDistance: float
    TotalTime: TimeSpan
}

/// Performance metrics for comparison
type Performance = {
    SolutionTime: TimeSpan
    TotalDistance: float
    Improvement: float option  // % improvement vs naive
}

// ============================================================================
// Real NYC Delivery Data
// ============================================================================

let warehouse = {
    Name = "QuickShip Warehouse - Manhattan"
    Latitude = 40.7589
    Longitude = -73.9851
}

let customers = [
    { Name = "Brooklyn Tech Hub"; Latitude = 40.6782; Longitude = -73.9442 }
    { Name = "Queens Distribution"; Latitude = 40.7282; Longitude = -73.7949 }
    { Name = "Bronx Medical Supply"; Latitude = 40.8448; Longitude = -73.8648 }
    { Name = "Upper East Side Boutique"; Latitude = 40.7739; Longitude = -73.9568 }
    { Name = "Staten Island Warehouse"; Latitude = 40.5795; Longitude = -74.1502 }
    { Name = "Jersey City Office"; Latitude = 40.7178; Longitude = -74.0431 }
    { Name = "Newark Distribution"; Latitude = 40.7357; Longitude = -74.1724 }
    { Name = "Yonkers Retail"; Latitude = 40.9312; Longitude = -73.8987 }
    { Name = "New Rochelle Store"; Latitude = 40.9115; Longitude = -73.7823 }
    { Name = "Paterson Industrial"; Latitude = 40.9168; Longitude = -74.1718 }
    { Name = "Elizabeth Port"; Latitude = 40.6640; Longitude = -74.2107 }
    { Name = "Edison Tech Center"; Latitude = 40.5187; Longitude = -74.4121 }
    { Name = "Woodbridge Logistics"; Latitude = 40.5576; Longitude = -74.2846 }
    { Name = "Lakewood Retail"; Latitude = 40.0979; Longitude = -74.2179 }
    { Name = "Toms River Distribution"; Latitude = 39.9537; Longitude = -74.1979 }
]

let allStops = warehouse :: customers

// ============================================================================
// Distance Calculations (Idiomatic F# with pure functions)
// ============================================================================

/// Calculate Haversine distance between two locations (in km)
let haversineDistance (loc1: Location) (loc2: Location) : float =
    let earthRadius = 6371.0  // Earth's radius in km
    let toRadians deg = deg * Math.PI / 180.0
    
    let lat1, lon1 = toRadians loc1.Latitude, toRadians loc1.Longitude
    let lat2, lon2 = toRadians loc2.Latitude, toRadians loc2.Longitude
    
    let dLat = lat2 - lat1
    let dLon = lon2 - lon1
    
    let a = 
        Math.Sin(dLat / 2.0) ** 2.0 + 
        Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2.0) ** 2.0
    
    let c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a))
    
    earthRadius * c

/// Calculate total distance for a route
let calculateRouteDistance (route: Location list) : float =
    route
    |> List.pairwise
    |> List.sumBy (fun (loc1, loc2) -> haversineDistance loc1 loc2)

/// Estimate driving time (assuming 40 km/h average in city traffic)
let estimateDrivingTime (distanceKm: float) : TimeSpan =
    let averageSpeedKmh = 40.0
    let hours = distanceKm / averageSpeedKmh
    TimeSpan.FromHours(hours)

// ============================================================================
// Solution Formatting (Idiomatic F# with active patterns)
// ============================================================================

/// Format distance with appropriate precision
let formatDistance (distance: float) : string =
    if distance < 50.0 then 
        sprintf "%.1f km" distance
    elif distance < 150.0 then 
        sprintf "%.0f km" distance
    else 
        sprintf "%.0f km" distance

/// Format time in readable format
let formatTime (time: TimeSpan) : string =
    if time.TotalHours >= 1.0 then
        sprintf "%.1f hours" time.TotalHours
    else
        sprintf "%d minutes" (int time.TotalMinutes)

/// Print route details (side effect clearly isolated)
let printRoute (label: string) (route: Route) (perf: Performance) (method: string option) : unit =
    printfn "\n%s" label
    printfn "  Distance: %s" (formatDistance route.TotalDistance)
    printfn "  Est. Time: %s" (formatTime route.TotalTime)
    printfn "  Solution Time: %.0fms" perf.SolutionTime.TotalMilliseconds
    
    match method with
    | Some m -> printfn "  Solver: %s" m
    | None -> ()
    
    match perf.Improvement with
    | Some improvement -> 
        printfn "  Improvement: %.1f%% better than naive route" improvement
    | None -> ()
    
    printfn "\n  Route:"
    route.Path 
    |> List.iteri (fun i loc -> 
        printfn "    %2d. %s" (i + 1) loc.Name
    )

// ============================================================================
// Solver Integration (Using HybridSolver for Quantum-Ready Optimization)
// ============================================================================

/// Build distance matrix from locations using Haversine distance
let buildDistanceMatrix (locations: Location list) : float[,] =
    let n = List.length locations
    Array2D.init n n (fun i j ->
        if i = j then 0.0
        else haversineDistance locations.[i] locations.[j]
    )

/// Convert TSP solution to Route domain type
let solutionToRoute (locations: Location list) (solution: HybridSolver.Solution<TspSolver.TspSolution>) : Route * Performance =
    // Extract tour from TSP solution (Tour is int array)
    let tourArray = solution.Result.Tour
    
    // Build path from tour indices
    let path = 
        tourArray 
        |> Array.map (fun cityIdx -> locations.[cityIdx])
        |> Array.toList
    
    // Add return to start for complete tour
    let completePath = path @ [List.head path]
    
    let distance = calculateRouteDistance completePath
    let time = estimateDrivingTime distance
    
    let route = { Path = completePath; TotalDistance = distance; TotalTime = time }
    let perf = {
        SolutionTime = TimeSpan.FromMilliseconds(solution.ElapsedMs)
        TotalDistance = distance
        Improvement = None  // Will be calculated later vs naive
    }
    
    (route, perf)

/// Solve TSP using HybridSolver (automatic classical/quantum routing)
let solveWithHybridSolver (locations: Location list) : Result<(Route * Performance * string), string> =
    let distances = buildDistanceMatrix locations
    
    // HybridSolver automatically decides classical vs quantum based on problem size
    match HybridSolver.solveTsp distances None None None with
    | Ok solution ->
        let (route, perf) = solutionToRoute locations solution
        // Return route, performance, and solver reasoning
        Ok (route, perf, solution.Reasoning)
    | Error err -> Error (sprintf "HybridSolver failed: %s" err.Message)

/// Calculate naive route (just visit in given order) for baseline
let calculateNaiveRoute (locations: Location list) : Route =
    let distance = calculateRouteDistance locations
    let time = estimateDrivingTime distance
    { Path = locations; TotalDistance = distance; TotalTime = time }

// ============================================================================
// Main Execution (Side effects isolated at top level)
// ============================================================================

printfn "╔═══════════════════════════════════════════════════════════════╗"
printfn "║     QuickShip Logistics - Delivery Route Optimization        ║"
printfn "╚═══════════════════════════════════════════════════════════════╝"
printfn ""
printfn "Business Problem:"
printfn "  Optimize daily delivery route for 15 customers in NYC area"
printfn "  Starting point: %s" warehouse.Name
printfn "  Customers: %d stops" customers.Length
printfn ""

// Calculate baseline (naive route)
let naiveRoute = calculateNaiveRoute allStops
printfn "📊 Baseline Analysis:"
printfn "  Naive route (visit in given order):"
printfn "    Distance: %s" (formatDistance naiveRoute.TotalDistance)
printfn "    Est. Time: %s" (formatTime naiveRoute.TotalTime)

// Solve with hybrid optimization (quantum-ready)
printfn "\n⚙️  Solving with HybridSolver (Quantum-Ready Optimization)..."

match solveWithHybridSolver allStops with
| Ok (optimizedRoute, perf, reasoning) ->
    // Calculate improvement
    let improvement = 
        (naiveRoute.TotalDistance - optimizedRoute.TotalDistance) / naiveRoute.TotalDistance * 100.0
    
    let perfWithImprovement = { perf with Improvement = Some improvement }
    
    printfn "\n💡 Solver Decision: %s" reasoning
    printRoute "✅ Optimized Route Found" optimizedRoute perfWithImprovement (Some "HybridSolver")
    
    // Business insights
    printfn "\n💡 Business Impact:"
    let fuelSavings = improvement
    let timeSavings = naiveRoute.TotalTime - optimizedRoute.TotalTime
    
    printfn "  • %.1f km shorter route (%.1f%% reduction)" 
        (naiveRoute.TotalDistance - optimizedRoute.TotalDistance) improvement
    printfn "  • %s faster delivery" (formatTime timeSavings)
    printfn "  • Estimated fuel savings: %.1f%% per day" fuelSavings
    printfn "  • Annual impact (250 work days): ~%.0f km saved" 
        ((naiveRoute.TotalDistance - optimizedRoute.TotalDistance) * 250.0)

| Error msg ->
    printfn "❌ Optimization failed: %s" msg
    printfn "\nUsing baseline naive route"
    let perf = {
        SolutionTime = TimeSpan.Zero
        TotalDistance = naiveRoute.TotalDistance
        Improvement = None
    }
    printRoute "Naive Route" naiveRoute perf None

// Additional Analysis
printfn "\n📈 Route Statistics:"
printfn "  Total stops: %d" allStops.Length
printfn "  Average distance between stops: %.1f km" 
    (naiveRoute.TotalDistance / float allStops.Length)

printfn "\n✨ Note: This example uses HybridSolver with automatic classical/quantum routing."
printfn "   Current problem size (16 cities) → Classical solver (fast, optimal for <50 cities)"
printfn "   For larger problems (50+ cities), quantum solvers may provide advantages."
printfn ""
