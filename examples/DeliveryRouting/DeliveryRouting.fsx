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
- Solution quality: Within 5-10% of optimal
- Typical improvement: 20-30% better than naive route
*)

#r "nuget: FSharp.Azure.Quantum, 0.5.0-beta"

open System
open FSharp.Azure.Quantum

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
let printRoute (label: string) (route: Route) (perf: Performance) : unit =
    printfn "\n%s" label
    printfn "  Distance: %s" (formatDistance route.TotalDistance)
    printfn "  Est. Time: %s" (formatTime route.TotalTime)
    printfn "  Solution Time: %.0fms" perf.SolutionTime.TotalMilliseconds
    
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
// Solver Integration (Idiomatic F# with Railway-Oriented Programming)
// ============================================================================

/// Convert locations to TSP problem format
let locationsToTspProblem (locations: Location list) =
    locations
    |> List.map (fun loc -> (loc.Name, loc.Latitude, loc.Longitude))

/// Convert TSP tour result to Route domain type
let tourToRoute (locations: Location list) (tour: int list) : Result<Route, string> =
    try
        let path = 
            tour 
            |> List.map (fun idx -> locations.[idx])
        
        let distance = calculateRouteDistance path
        let time = estimateDrivingTime distance
        
        Ok { Path = path; TotalDistance = distance; TotalTime = time }
    with ex ->
        Error (sprintf "Failed to convert tour: %s" ex.Message)

/// Solve TSP using classical solver (pure function returning Result)
let solveClassical (locations: Location list) : Result<Route * Performance, string> =
    let sw = Diagnostics.Stopwatch.StartNew()
    let problem = locationsToTspProblem locations
    
    match TSP.solveDirectly problem None with
    | Ok tour ->
        sw.Stop()
        
        // Convert Tour.Cities (string list) to indices for tourToRoute
        let tourIndices = 
            tour.Cities
            |> List.map (fun cityName -> 
                locations |> List.findIndex (fun loc -> loc.Name = cityName))
        
        match tourToRoute locations tourIndices with
        | Ok route ->
            let perf = {
                SolutionTime = sw.Elapsed
                TotalDistance = route.TotalDistance
                Improvement = None  // No baseline yet
            }
            Ok (route, perf)
        | Error msg -> Error msg
    
    | Error msg -> Error (sprintf "TSP solver failed: %s" msg)

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

// Solve with classical optimization
printfn "\n⚙️  Solving with Classical Optimization..."

match solveClassical allStops with
| Ok (optimizedRoute, perf) ->
    // Calculate improvement
    let improvement = 
        (naiveRoute.TotalDistance - optimizedRoute.TotalDistance) / naiveRoute.TotalDistance * 100.0
    
    let perfWithImprovement = { perf with Improvement = Some improvement }
    
    printRoute "✅ Optimized Route Found" optimizedRoute perfWithImprovement
    
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
    printRoute "Naive Route" naiveRoute perf

// Additional Analysis
printfn "\n📈 Route Statistics:"
printfn "  Total stops: %d" allStops.Length
printfn "  Average distance between stops: %.1f km" 
    (naiveRoute.TotalDistance / float allStops.Length)

printfn "\n✨ Note: This example uses classical solver for fast execution."
printfn "   For larger problems (50+ cities), quantum solvers may provide benefits."
printfn ""
