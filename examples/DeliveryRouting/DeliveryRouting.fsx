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
- Variables: Binary xáµ¢â±¼ (1 if edge iâ†’j is in tour, 0 otherwise)
- Objective: Minimize Î£áµ¢â±¼ dáµ¢â±¼ Ã— xáµ¢â±¼ (total distance)
- Constraints: Each city visited exactly once, no subtours

**Expected Performance**:
- Classical solver: < 100ms for 16 stops
- Quantum solver: Potential advantage for 50+ cities
- Solution quality: Within 5-10% of optimal
- Typical improvement: 20-30% better than naive route

**Quantum-Ready**: This example uses the HybridSolver which automatically routes
between classical (fast, free) and quantum (scalable) solvers based on problem size.

Usage:
  dotnet fsi DeliveryRouting.fsx                                  (defaults)
  dotnet fsi DeliveryRouting.fsx -- --help                        (show options)
  dotnet fsi DeliveryRouting.fsx -- --input locations.csv
  dotnet fsi DeliveryRouting.fsx -- --output route.json --csv route.csv
  dotnet fsi DeliveryRouting.fsx -- --quiet --output route.json   (pipeline mode)
  dotnet fsi DeliveryRouting.fsx -- --svg [path]                  (also draw the route as an animated SVG,
                                                                   default _images/delivery-routing.svg)
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

TSP maps to QUBO using binary variables xáµ¢,â‚œ âˆˆ {0,1} indicating city i is visited
at time t. Constraints ensure: (1) each city visited once: Î£â‚œ xáµ¢,â‚œ = 1, (2) each
time has one city: Î£áµ¢ xáµ¢,â‚œ = 1. The objective minimizes Î£áµ¢â±¼â‚œ dáµ¢â±¼Â·xáµ¢,â‚œÂ·xâ±¼,â‚œâ‚Šâ‚.
This quadratic form suits QAOA, which explores tours in superposition. The Vehicle
Routing Problem (VRP) generalizes TSP to multiple vehicles with capacity constraints.

Key Equations:
  - Tour length: L = Î£â‚–â‚Œâ‚â¿ d(Ï€â‚–, Ï€â‚–â‚Šâ‚) where Ï€ is a permutation of cities
  - QUBO variables: xáµ¢,â‚œ = 1 iff city i visited at position t
  - Row constraint: Î£â‚œ xáµ¢,â‚œ = 1 for each city i
  - Column constraint: Î£áµ¢ xáµ¢,â‚œ = 1 for each time t
  - Objective: Î£áµ¢â±¼â‚œ dáµ¢â±¼Â·xáµ¢,â‚œÂ·xâ±¼,â‚œâ‚Šâ‚ (distance between consecutive cities)
  - Held-Karp DP: O(nÂ²Â·2â¿) exact solution (classical baseline)

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

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
// The library comes from NuGet; `dotnet fsi --define:LOCAL_BUILD <script>` uses the repo's Debug build.
#if LOCAL_BUILD
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#else
#r "nuget: FSharp.Azure.Quantum"
#endif

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"
#load "../_common/SvgAnimation.fsx"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Classical
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "DeliveryRouting.fsx"
    "Quantum-ready delivery route optimization (TSP) using HybridSolver."
    [
        {
            Cli.OptionSpec.Name = "input"
            Description = "CSV file with locations (name,latitude,longitude)"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "output"
            Description = "Write results to JSON file"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "csv"
            Description = "Write results to CSV file"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "quiet"
            Description = "Suppress informational output"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "svg"
            Description = "Also draw the route as an animated SVG (optional path)"
            Default = Some "_images/delivery-routing.svg"
        }
    ]
    args

let quiet = Cli.hasFlag "quiet" args
let inputPath = Cli.tryGet "input" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

// ============================================================================
// Domain Model (Idiomatic F#)
// ============================================================================

/// Geographic location with name and coordinates
type Location =
    {
        Name: string
        Latitude: float
        Longitude: float
    }

/// Route solution with distance and path
type Route =
    {
        Path: Location list
        TotalDistance: float
        TotalTime: TimeSpan
    }

/// Performance metrics for comparison
type Performance =
    {
        SolutionTime: TimeSpan
        TotalDistance: float
        Improvement: float option // % improvement vs naive
    }

// ============================================================================
// Real NYC Delivery Data (or load from file)
// ============================================================================

let builtInWarehouse =
    {
        Name = "QuickShip Warehouse - Manhattan"
        Latitude = 40.7589
        Longitude = -73.9851
    }

let builtInCustomers =
    [
        {
            Name = "Brooklyn Tech Hub"
            Latitude = 40.6782
            Longitude = -73.9442
        }
        {
            Name = "Queens Distribution"
            Latitude = 40.7282
            Longitude = -73.7949
        }
        {
            Name = "Bronx Medical Supply"
            Latitude = 40.8448
            Longitude = -73.8648
        }
        {
            Name = "Upper East Side Boutique"
            Latitude = 40.7739
            Longitude = -73.9568
        }
        {
            Name = "Staten Island Warehouse"
            Latitude = 40.5795
            Longitude = -74.1502
        }
        {
            Name = "Jersey City Office"
            Latitude = 40.7178
            Longitude = -74.0431
        }
        {
            Name = "Newark Distribution"
            Latitude = 40.7357
            Longitude = -74.1724
        }
        {
            Name = "Yonkers Retail"
            Latitude = 40.9312
            Longitude = -73.8987
        }
        {
            Name = "New Rochelle Store"
            Latitude = 40.9115
            Longitude = -73.7823
        }
        {
            Name = "Paterson Industrial"
            Latitude = 40.9168
            Longitude = -74.1718
        }
        {
            Name = "Elizabeth Port"
            Latitude = 40.6640
            Longitude = -74.2107
        }
        {
            Name = "Edison Tech Center"
            Latitude = 40.5187
            Longitude = -74.4121
        }
        {
            Name = "Woodbridge Logistics"
            Latitude = 40.5576
            Longitude = -74.2846
        }
        {
            Name = "Lakewood Retail"
            Latitude = 40.0979
            Longitude = -74.2179
        }
        {
            Name = "Toms River Distribution"
            Latitude = 39.9537
            Longitude = -74.1979
        }
    ]

/// Load locations from a CSV file with columns: name, latitude, longitude
/// First row is treated as the warehouse/depot; remaining rows are customers.
let loadLocationsFromCsv (path: string) : Location * Location list =
    let rows = Data.readCsvWithHeader path

    let toLocation (row: Data.CsvRow) : Location =
        {
            Name = row.Values |> Map.tryFind "name" |> Option.defaultValue "Unknown"
            Latitude =
                row.Values
                |> Map.tryFind "latitude"
                |> Option.bind (fun s ->
                    match Double.TryParse s with
                    | true, v -> Some v
                    | _ -> None)
                |> Option.defaultValue 0.0
            Longitude =
                row.Values
                |> Map.tryFind "longitude"
                |> Option.bind (fun s ->
                    match Double.TryParse s with
                    | true, v -> Some v
                    | _ -> None)
                |> Option.defaultValue 0.0
        }

    match rows with
    | [] -> failwith $"Input CSV is empty, calling loadLocationsFromCsv with path: {path}"
    | depot :: rest -> (toLocation depot, rest |> List.map toLocation)

let warehouse, customers =
    match inputPath with
    | Some path ->
        let resolved = Data.resolveRelative __SOURCE_DIRECTORY__ path

        if not quiet then
            printfn "Loading locations from: %s" resolved

        loadLocationsFromCsv resolved
    | None -> builtInWarehouse, builtInCustomers

let allStops = warehouse :: customers

// ============================================================================
// Distance Calculations (Idiomatic F# with pure functions)
// ============================================================================

/// Calculate Haversine distance between two locations (in km)
let haversineDistance (loc1: Location) (loc2: Location) : float =
    let earthRadius = 6371.0 // Earth's radius in km
    let toRadians deg = deg * Math.PI / 180.0

    let lat1, lon1 = toRadians loc1.Latitude, toRadians loc1.Longitude
    let lat2, lon2 = toRadians loc2.Latitude, toRadians loc2.Longitude

    let dLat = lat2 - lat1
    let dLon = lon2 - lon1

    let a =
        Math.Sin(dLat / 2.0) ** 2.0
        + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2.0) ** 2.0

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
    if distance < 50.0 then $"%.1f{distance} km"
    elif distance < 150.0 then $"%.0f{distance} km"
    else $"%.0f{distance} km"

/// Format time in readable format
let formatTime (time: TimeSpan) : string =
    if time.TotalHours >= 1.0 then
        $"%.1f{time.TotalHours} hours"
    else
        sprintf "%d minutes" (int time.TotalMinutes)

/// Print route details (side effect clearly isolated)
let printRoute (label: string) (route: Route) (perf: Performance) (method: string option) : unit =
    if not quiet then
        printfn "\n%s" label
        printfn "  Distance: %s" (formatDistance route.TotalDistance)
        printfn "  Est. Time: %s" (formatTime route.TotalTime)
        printfn "  Solution Time: %.0fms" perf.SolutionTime.TotalMilliseconds

        match method with
        | Some m -> printfn "  Solver: %s" m
        | None -> ()

        match perf.Improvement with
        | Some improvement -> printfn "  Improvement: %.1f%% better than naive route" improvement
        | None -> ()

        printfn "\n  Route:"
        route.Path |> List.iteri (fun i loc -> printfn "    %2d. %s" (i + 1) loc.Name)

// ============================================================================
// Solver Integration (Using HybridSolver for Quantum-Ready Optimization)
// ============================================================================

/// Build distance matrix from locations using Haversine distance
let buildDistanceMatrix (locations: Location list) : float[,] =
    let n = List.length locations

    Array2D.init n n (fun i j ->
        if i = j then
            0.0
        else
            haversineDistance locations.[i] locations.[j])

/// Convert TSP solution to Route domain type
let solutionToRoute
    (locations: Location list)
    (solution: HybridSolver.Solution<TspSolver.TspSolution>)
    : Route * Performance =
    // Extract tour from TSP solution (Tour is int array)
    let tourArray = solution.Result.Tour

    // Build path from tour indices
    let path =
        tourArray |> Array.map (fun cityIdx -> locations.[cityIdx]) |> Array.toList

    // Add return to start for complete tour
    let completePath = path @ [ List.head path ]

    let distance = calculateRouteDistance completePath
    let time = estimateDrivingTime distance

    let route =
        {
            Path = completePath
            TotalDistance = distance
            TotalTime = time
        }

    let perf =
        {
            SolutionTime = TimeSpan.FromMilliseconds(solution.ElapsedMs)
            TotalDistance = distance
            Improvement = None // Will be calculated later vs naive
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
        Ok(route, perf, solution.Reasoning)
    | Error err -> Error $"HybridSolver failed: %s{err.Message}"

/// Calculate naive route (just visit in given order) for baseline
let calculateNaiveRoute (locations: Location list) : Route =
    let distance = calculateRouteDistance locations
    let time = estimateDrivingTime distance

    {
        Path = locations
        TotalDistance = distance
        TotalTime = time
    }

// ============================================================================
// Main Execution (Side effects isolated at top level)
// ============================================================================

if not quiet then
    printfn
        "â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—"

    printfn "â•‘     QuickShip Logistics - Delivery Route Optimization        â•‘"

    printfn
        "â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•"

    printfn ""
    printfn "Business Problem:"
    printfn "  Optimize daily delivery route for 15 customers in NYC area"
    printfn "  Starting point: %s" warehouse.Name
    printfn "  Customers: %d stops" customers.Length
    printfn ""

// Calculate baseline (naive route)
let naiveRoute = calculateNaiveRoute allStops

if not quiet then
    printfn "ðŸ“Š Baseline Analysis:"
    printfn "  Naive route (visit in given order):"
    printfn "    Distance: %s" (formatDistance naiveRoute.TotalDistance)
    printfn "    Est. Time: %s" (formatTime naiveRoute.TotalTime)

// Solve with hybrid optimization (quantum-ready)
if not quiet then
    printfn "\nâš™ï¸  Solving with HybridSolver (Quantum-Ready Optimization)..."

let solverResult = solveWithHybridSolver allStops

let resultRoute, resultPerf, resultSolver =
    match solverResult with
    | Ok(optimizedRoute, perf, reasoning) ->
        let improvement =
            (naiveRoute.TotalDistance - optimizedRoute.TotalDistance)
            / naiveRoute.TotalDistance
            * 100.0

        let perfWithImprovement =
            { perf with
                Improvement = Some improvement
            }

        if not quiet then
            printfn "\nðŸ’¡ Solver Decision: %s" reasoning
            printRoute "âœ… Optimized Route Found" optimizedRoute perfWithImprovement (Some "HybridSolver")

            // Business insights
            printfn "\nðŸ’¡ Business Impact:"
            let fuelSavings = improvement
            let timeSavings = naiveRoute.TotalTime - optimizedRoute.TotalTime

            printfn
                "  â€¢ %.1f km shorter route (%.1f%% reduction)"
                (naiveRoute.TotalDistance - optimizedRoute.TotalDistance)
                improvement

            printfn "  â€¢ %s faster delivery" (formatTime timeSavings)
            printfn "  â€¢ Estimated fuel savings: %.1f%% per day" fuelSavings

            printfn
                "  â€¢ Annual impact (250 work days): ~%.0f km saved"
                ((naiveRoute.TotalDistance - optimizedRoute.TotalDistance) * 250.0)

        (optimizedRoute, perfWithImprovement, "HybridSolver")

    | Error msg ->
        if not quiet then
            printfn "âŒ Optimization failed: %s" msg
            printfn "\nUsing baseline naive route"

        let perf =
            {
                SolutionTime = TimeSpan.Zero
                TotalDistance = naiveRoute.TotalDistance
                Improvement = None
            }

        if not quiet then
            printRoute "Naive Route" naiveRoute perf None

        (naiveRoute, perf, "Fallback (naive)")

// Additional Analysis
if not quiet then
    printfn "\nðŸ“ˆ Route Statistics:"
    printfn "  Total stops: %d" allStops.Length
    printfn "  Average distance between stops: %.1f km" (naiveRoute.TotalDistance / float allStops.Length)

    printfn "\nâœ¨ Note: This example uses HybridSolver with automatic classical/quantum routing."
    printfn "   Current problem size (16 cities) â†’ Classical solver (fast, optimal for <50 cities)"
    printfn "   For larger problems (50+ cities), quantum solvers may provide advantages."
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let routeStops =
    resultRoute.Path |> List.map (fun loc -> loc.Name) |> String.concat " â†’ "

let resultRows: Map<string, string> list =
    [
        Map.ofList
            [
                "method", resultSolver
                "total_distance_km", $"%.2f{resultRoute.TotalDistance}"
                "estimated_time_hours", $"%.2f{resultRoute.TotalTime.TotalHours}"
                "num_stops", $"%d{allStops.Length}"
                "improvement_pct",
                match resultPerf.Improvement with
                | Some pct -> $"%.1f{pct}"
                | None -> "N/A"
                "solution_time_ms", $"%.0f{resultPerf.SolutionTime.TotalMilliseconds}"
                "naive_distance_km", $"%.2f{naiveRoute.TotalDistance}"
                "route", routeStops
            ]
    ]

match outputPath with
| Some path ->
    Reporting.writeJson path resultRows

    if not quiet then
        printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [
            "method"
            "total_distance_km"
            "estimated_time_hours"
            "num_stops"
            "improvement_pct"
            "solution_time_ms"
            "naive_distance_km"
            "route"
        ]

    let rows =
        resultRows
        |> List.map (fun m -> header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))

    Reporting.writeCsv path header rows

    if not quiet then
        printfn "Results written to %s" path
| None -> ()

// ==============================================================================
// PICTURE (only with --svg): the van driving the solved tour
// ==============================================================================

open SvgAnimation

/// The route as driven: a closed tour turned to start and end at the depot.
let drivenRoute =
    let path = resultRoute.Path
    let closed = path.Length > 2 && List.head path = List.last path

    match List.tryFindIndex ((=) warehouse) path with
    | Some i when closed && i > 0 ->
        let cycle = List.take (path.Length - 1) path
        List.skip i cycle @ List.take i cycle @ [ warehouse ]
    | _ -> path

let drawDeliveryPicture (file: string) =
    let stops = Array.ofList drivenRoute
    let n = stops.Length
    let legKm = Array.init (n - 1) (fun i -> haversineDistance stops.[i] stops.[i + 1])
    let atKm = Array.scan (+) 0.0 legKm
    let totalKm = atKm.[n - 1]
    let hoursAt km = (estimateDrivingTime km).TotalHours

    let clock km =
        let minutes = int (Math.Round(hoursAt km * 60.0))
        sprintf "%d:%02d h" (minutes / 60) (minutes % 60)

    let isDepot (l: Location) = l = warehouse
    let customerCount = stops.[1..] |> Array.filter (isDepot >> not) |> Array.length

    // Local map: kilometres east and north of the stops' south-west corner.
    let midLat = (allStops |> List.averageBy (fun l -> l.Latitude)) * Math.PI / 180.0

    let kmEast = 111.32 * cos midLat
    let kmNorth = 110.57
    let minLon = allStops |> List.map (fun l -> l.Longitude) |> List.min
    let maxLon = allStops |> List.map (fun l -> l.Longitude) |> List.max
    let minLat = allStops |> List.map (fun l -> l.Latitude) |> List.min
    let maxLat = allStops |> List.map (fun l -> l.Latitude) |> List.max
    let widthKm = max 1e-3 ((maxLon - minLon) * kmEast)
    let heightKm = max 1e-3 ((maxLat - minLat) * kmNorth)
    let boxX, boxY, boxW, boxH = 24.0, 66.0, 256.0, 452.0
    let pxPerKm = min ((boxW - 40.0) / widthKm) ((boxH - 40.0) / heightKm)
    let originX = boxX + (boxW - widthKm * pxPerKm) / 2.0
    let originY = boxY + (boxH - heightKm * pxPerKm) / 2.0

    let at (l: Location) =
        (originX + (l.Longitude - minLon) * kmEast * pxPerKm, originY + (maxLat - l.Latitude) * kmNorth * pxPerKm)

    let points = stops |> Array.map at

    // Frames: two at the depot, then each leg in frames in proportion to its
    // length (at least one, so every stop is a frame), then four back home.
    let movingFrames = 72

    let legFrames =
        legKm
        |> Array.map (fun km -> max 1 (int (Math.Round(km / max totalKm 1e-9 * float movingFrames))))

    /// Per frame: km driven and the leg the van is on.
    let samples =
        [|
            yield! Array.replicate 2 (0.0, 0)

            for leg in 0 .. n - 2 do
                for j in 1 .. legFrames.[leg] do
                    yield (atKm.[leg] + legKm.[leg] * float j / float legFrames.[leg], leg)

            yield! Array.replicate 4 (totalKm, n - 2)
        |]

    let frames = samples.Length

    let position (km, leg) =
        let x0, y0 = points.[leg]
        let x1, y1 = points.[leg + 1]

        let f =
            if legKm.[leg] > 0.0 then
                (km - atKm.[leg]) / legKm.[leg]
            else
                1.0

        (x0 + (x1 - x0) * f, y0 + (y1 - y0) * f)

    let vanAt = samples |> Array.map position

    // First frame at which each stop has been reached.
    let reachedFrame =
        Array.init n (fun s -> samples |> Array.findIndex (fun (km, _) -> km >= atKm.[s] - 1e-9))

    let pic =
        Picture(
            760.0,
            600.0,
            frames,
            18.0,
            $"Delivery route: one van, %d{customerCount} customers, %.0f{totalKm} km",
            hold = 0.05
        )

    let accent = colour 0
    let served = colour 2

    pic.Text(
        24.0,
        28.0,
        $"Delivery route: one van, %d{customerCount} customers, %.0f{totalKm} km (%s{resultSolver})",
        size = 17.0,
        bold = true
    )

    pic.Text(
        24.0,
        47.0,
        sprintf
            "The solved tour; the van moves at the script's %.0f km/h on straight-line legs. No loads or time windows in this model."
            (totalKm / max (hoursAt totalKm) 1e-9),
        size = 11.5,
        fill = grey
    )

    // Map: frame, scale bar, the planned tour as a pale band, the driven part on top.
    pic.Rect(boxX, boxY, boxW, boxH, fill = panel, stroke = frameColour, rx = 4.0)

    let barKm =
        [ 1.0; 2.0; 5.0; 10.0; 20.0; 50.0; 100.0; 200.0; 500.0 ]
        |> List.filter (fun k -> k * pxPerKm <= boxW / 3.0)
        |> List.tryLast
        |> Option.defaultValue 1.0

    pic.Line(
        boxX + 12.0,
        boxY + boxH - 14.0,
        boxX + 12.0 + barKm * pxPerKm,
        boxY + boxH - 14.0,
        stroke = grey,
        width = 2.0
    )

    pic.Text(boxX + 12.0, boxY + boxH - 20.0, $"%.0f{barKm} km", size = 10.0, fill = grey)

    let polyline (pts: (float * float)[]) =
        pts
        |> Array.mapi (fun i (x, y) -> sprintf "%s%s %s" (if i = 0 then "M" else " L") (num x) (num y))
        |> String.concat ""

    pic.Path(polyline points, stroke = tint 0.7 accent, width = 8.0)

    // The driven part: the whole tour drawn as one dash that grows with the
    // distance driven, measured along the drawn line.
    let legPx =
        Array.init (n - 1) (fun i ->
            let (x0, y0), (x1, y1) = points.[i], points.[i + 1]
            sqrt ((x1 - x0) ** 2.0 + (y1 - y0) ** 2.0))

    let atPx = Array.scan (+) 0.0 legPx
    let tourPx = atPx.[n - 1] + 1.0

    let drivenPx =
        samples
        |> Array.map (fun (km, leg) ->
            let f =
                if legKm.[leg] > 0.0 then
                    (km - atKm.[leg]) / legKm.[leg]
                else
                    1.0

            atPx.[leg] + legPx.[leg] * f)

    pic.Element(
        "path",
        [
            "d", polyline points
            "stroke", accent
            "stroke-width", "2.5"
            "fill", "none"
            "stroke-linejoin", "round"
            "stroke-dasharray", sprintf "%s %s" (num tourPx) (num tourPx)
        ],
        animate = [ ("stroke-dashoffset", drivenPx |> Array.map (fun d -> num (tourPx - d))) ]
    )

    // Stops: numbered in visit order, green once the van has reached them.
    for s in 1 .. n - 1 do
        let x, y = points.[s]

        if not (isDepot stops.[s]) then
            pic.Circle(x, y, 7.5, fill = "white", stroke = grey, width = 1.2)

            pic.Circle(
                x,
                y,
                7.5,
                fill = tint 0.45 served,
                stroke = served,
                width = 1.5,
                animate =
                    [
                        ("opacity", Array.init frames (fun f -> if f >= reachedFrame.[s] then 1.0 else 0.0))
                    ]
            )

            pic.Text(x, y + 3.2, string s, size = 9.0, anchor = "middle", bold = true)

    let depotX, depotY = points.[0]
    pic.Rect(depotX - 7.0, depotY - 7.0, 14.0, 14.0, fill = ink, rx = 2.0)
    pic.Text(depotX + 11.0, depotY - 8.0, "Depot", size = 10.0, bold = true)

    // The van: a small side view, facing the way it drives.
    let van (x: float, y: float) (facing: float) =
        let d v = num (v * facing)

        sprintf
            "M%s %s v-8 h%s v2 h%s l%s 3 v3 z m%s 0 a2 2 0 1 0 %s 0 a2 2 0 1 0 %s 0 m%s 0 a2 2 0 1 0 %s 0 a2 2 0 1 0 %s 0"
            (num (x - 9.0 * facing))
            (num (y + 3.0))
            (d 10.0)
            (d 4.0)
            (d 3.0)
            (d 4.0)
            (d 4.0)
            (d -4.0)
            (d 9.0)
            (d 4.0)
            (d -4.0)

    let facings =
        samples
        |> Array.scan
            (fun previous (_, leg) ->
                let dx = fst points.[leg + 1] - fst points.[leg]
                if abs dx < 0.5 then previous else float (sign dx))
            1.0
        |> Array.skip 1

    let vans = Array.init frames (fun f -> van vanAt.[f] facings.[f])
    pic.Path(vans.[0], stroke = "white", width = 1.0, fill = accent, shapes = vans)

    // Visit list: the stop the van is heading for is shaded.
    let listX, listTop = 300.0, 92.0
    let rowH = min 21.0 (340.0 / float (max 1 (n - 1)))
    let rowY s = listTop + float (s - 1) * rowH

    pic.Text(listX, 76.0, "Visit order", size = 13.0, bold = true)
    pic.Text(736.0, 76.0, "arrives after", size = 10.0, fill = grey, anchor = "end")

    let heading =
        samples
        |> Array.map (fun (km, _) ->
            [ 1 .. n - 1 ]
            |> List.tryFind (fun s -> atKm.[s] > km + 1e-9)
            |> Option.defaultValue (n - 1))

    pic.Rect(
        listX - 4.0,
        rowY heading.[0] - rowH + 5.0,
        444.0,
        rowH,
        fill = tint 0.85 accent,
        rx = 3.0,
        animate = [ ("y", heading |> Array.map (fun s -> rowY s - rowH + 5.0)) ]
    )

    for s in 1 .. n - 1 do
        let y = rowY s

        let name =
            if isDepot stops.[s] then
                "Back at the depot"
            else
                stops.[s].Name

        let reached = Array.init frames (fun f -> f >= reachedFrame.[s])
        pic.Text(listX, y, string s, size = 11.0, fill = grey)

        pic.FrameText(
            listX + 20.0,
            y,
            reached |> Array.map (fun r -> if r then "✓" else ""),
            size = 12.0,
            fill = served,
            bold = true
        )

        pic.FrameText(listX + 36.0, y, reached |> Array.map (fun r -> if r then "" else name), size = 11.5, fill = grey)
        pic.FrameText(listX + 36.0, y, reached |> Array.map (fun r -> if r then name else ""), size = 11.5)
        pic.Text(736.0, y, clock atKm.[s], size = 11.0, fill = grey, anchor = "end")

    // Clock and odometer.
    let statsY = listTop + 340.0 + 22.0

    pic.FrameText(
        listX,
        statsY,
        samples |> Array.map (fun (km, _) -> "Drive time " + clock km),
        size = 16.0,
        bold = true
    )

    pic.FrameText(
        736.0,
        statsY,
        Array.init frames (fun f ->
            let done' =
                [ 1 .. n - 1 ]
                |> List.filter (fun s -> f >= reachedFrame.[s] && not (isDepot stops.[s]))
                |> List.length

            $"%d{done'} of %d{customerCount} customers served"),
        size = 12.0,
        anchor = "end"
    )

    pic.Rect(listX, statsY + 10.0, 436.0, 8.0, fill = light, rx = 4.0)

    pic.Rect(
        listX,
        statsY + 10.0,
        0.0,
        8.0,
        fill = accent,
        rx = 4.0,
        animate =
            [
                ("width", samples |> Array.map (fun (km, _) -> 436.0 * km / max totalKm 1e-9))
            ]
    )

    pic.Text(listX, statsY + 32.0, "distance driven", size = 10.0, fill = grey)
    pic.Text(736.0, statsY + 32.0, $"%.0f{totalKm} km", size = 10.0, fill = grey, anchor = "end")

    // Legend.
    let legendY = 536.0
    pic.Line(30.0, legendY, 52.0, legendY, stroke = tint 0.7 accent, width = 8.0)
    pic.Text(58.0, legendY + 4.0, "planned tour", size = 11.0, fill = grey)
    pic.Line(150.0, legendY, 172.0, legendY, stroke = accent, width = 2.5)
    pic.Text(178.0, legendY + 4.0, "driven", size = 11.0, fill = grey)
    pic.Path(van (240.0, legendY) 1.0, stroke = "white", width = 1.0, fill = accent)
    pic.Text(254.0, legendY + 4.0, "van", size = 11.0, fill = grey)
    pic.Circle(300.0, legendY, 7.0, fill = "white", stroke = grey, width = 1.2)
    pic.Text(312.0, legendY + 4.0, "stop, visit order", size = 11.0, fill = grey)
    pic.Circle(424.0, legendY, 7.0, fill = tint 0.45 served, stroke = served, width = 1.5)
    pic.Text(436.0, legendY + 4.0, "served", size = 11.0, fill = grey)
    pic.Rect(492.0, legendY - 7.0, 14.0, 14.0, fill = ink, rx = 2.0)
    pic.Text(512.0, legendY + 4.0, "depot", size = 11.0, fill = grey)
    pic.Rect(560.0, legendY - 6.0, 26.0, 12.0, fill = tint 0.85 accent, rx = 3.0)
    pic.Text(592.0, legendY + 4.0, "next stop", size = 11.0, fill = grey)

    pic.Progress(24.0, 566.0, 712.0, fill = grey)

    pic.Text(
        24.0,
        588.0,
        "Legs: great-circle km between the stops' coordinates. Drive time: distance over speed, no time at the stops.",
        size = 10.0,
        fill = grey
    )

    pic.Save file

match svgPath (IO.Path.Combine(__SOURCE_DIRECTORY__, "_images", "delivery-routing.svg")) with
| Some file -> drawDeliveryPicture file
| None -> ()

// ==============================================================================
// USAGE HINTS
// ==============================================================================

if argv.Length = 0 && not quiet then
    printfn "ðŸ’¡ Tip: Run with --help to see all options:"
    printfn "   dotnet fsi DeliveryRouting.fsx -- --help"
    printfn "   dotnet fsi DeliveryRouting.fsx -- --input locations.csv --output route.json"
    printfn "   dotnet fsi DeliveryRouting.fsx -- --quiet --output route.json  (pipeline mode)"
    printfn ""
