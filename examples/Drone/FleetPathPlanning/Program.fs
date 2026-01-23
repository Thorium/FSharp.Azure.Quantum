/// Drone Fleet Path Planning Example
/// 
/// This example demonstrates how to use FSharp.Azure.Quantum's TSP solvers
/// to optimize flight paths for a drone fleet visiting multiple waypoints.
/// 
/// DRONE DOMAIN MAPPING:
/// - Waypoints (delivery points, inspection sites) → TSP cities
/// - Drone flight distance → TSP edge weights
/// - Optimal visitation order → TSP tour
/// 
/// USE CASES:
/// - Delivery drone route optimization
/// - Agricultural inspection path planning
/// - Search and rescue area coverage
/// - Infrastructure inspection tours
/// 
/// QUANTUM ADVANTAGE:
/// - Classical TSP: O(n!) brute force, O(n² 2^n) dynamic programming
/// - Quantum QAOA: Polynomial speedup for large instances (>100 waypoints)
/// - HybridSolver automatically selects best approach based on problem size
namespace FSharp.Azure.Quantum.Examples.Drone.FleetPathPlanning

open System
open System.Diagnostics
open System.IO

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

open FSharp.Azure.Quantum.Examples.Common
open FSharp.Azure.Quantum.Examples.Drone.Domain

// =============================================================================
// DOMAIN TYPES
// =============================================================================

/// Geographic coordinate for a waypoint
type GeoCoordinate = {
    Latitude: float
    Longitude: float
    AltitudeMeters: float
}

/// A waypoint in the drone mission
type Waypoint = {
    Id: string
    Name: string
    Location: GeoCoordinate
    Priority: int
}

/// A drone in the fleet
type Drone = {
    Id: string
    Model: string
    MaxRangeKm: float
    MaxPayloadKg: float
    BatteryCapacityWh: float
    CruiseSpeedMs: float
}

/// Result of path optimization
type OptimizedRoute = {
    DroneId: string
    Waypoints: Waypoint list
    TotalDistanceKm: float
    EstimatedFlightTimeMin: float
    EnergyConsumptionWh: float
}

// =============================================================================
// DISTANCE CALCULATIONS
// =============================================================================

module Geography =
    /// Convert degrees to radians
    let toRadians (degrees: float) = degrees * Math.PI / 180.0
    
    /// Calculate Haversine distance between two coordinates (in km)
    /// This is the great-circle distance accounting for Earth's curvature
    let haversineDistance (p1: GeoCoordinate) (p2: GeoCoordinate) : float =
        let lat1, lon1 = toRadians p1.Latitude, toRadians p1.Longitude
        let lat2, lon2 = toRadians p2.Latitude, toRadians p2.Longitude
        
        let dLat = lat2 - lat1
        let dLon = lon2 - lon1
        
        let a = 
            Math.Sin(dLat / 2.0) ** 2.0 +
            Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2.0) ** 2.0
        let c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a))
        
        Environment.earthRadiusKm * c
    
    /// Calculate 3D distance including altitude difference
    let distance3D (p1: GeoCoordinate) (p2: GeoCoordinate) : float =
        let horizontalKm = haversineDistance p1 p2
        let verticalKm = abs (p1.AltitudeMeters - p2.AltitudeMeters) / 1000.0
        Math.Sqrt(horizontalKm ** 2.0 + verticalKm ** 2.0)

// =============================================================================
// DATA PARSING
// =============================================================================

module Parse =
    let private tryGet (k: string) (row: Data.CsvRow) =
        row.Values |> Map.tryFind k |> Option.map (fun s -> s.Trim())
    
    let private tryFloat (s: string option) =
        match s with
        | None -> None
        | Some v when String.IsNullOrWhiteSpace v -> None
        | Some v ->
            match Double.TryParse v with
            | true, x -> Some x
            | false, _ -> None
    
    let private tryInt (s: string option) =
        match s with
        | None -> None
        | Some v when String.IsNullOrWhiteSpace v -> None
        | Some v ->
            match Int32.TryParse v with
            | true, x -> Some x
            | false, _ -> None
    
    let readWaypoints (path: string) : Waypoint list * string list =
        let rows, structuralErrors = Data.readCsvWithHeaderWithErrors path
        
        let waypoints, errors =
            rows
            |> List.mapi (fun i row ->
                let rowNum = i + 2
                match tryGet "waypoint_id" row,
                      tryGet "name" row,
                      tryFloat (tryGet "latitude" row),
                      tryFloat (tryGet "longitude" row),
                      tryFloat (tryGet "altitude_m" row),
                      tryInt (tryGet "priority" row) with
                | Some id, Some name, Some lat, Some lon, Some alt, Some pri ->
                    Ok {
                        Id = id
                        Name = name
                        Location = { Latitude = lat; Longitude = lon; AltitudeMeters = alt }
                        Priority = pri
                    }
                | _ -> Error (sprintf "row=%d missing or invalid waypoint fields" rowNum))
            |> List.fold
                (fun (oks, errs) r ->
                    match r with
                    | Ok v -> (v :: oks, errs)
                    | Error e -> (oks, e :: errs))
                ([], [])
        
        (List.rev waypoints, structuralErrors @ (List.rev errors))
    
    let readDrones (path: string) : Drone list * string list =
        let rows, structuralErrors = Data.readCsvWithHeaderWithErrors path
        
        let drones, errors =
            rows
            |> List.mapi (fun i row ->
                let rowNum = i + 2
                match tryGet "drone_id" row,
                      tryGet "model" row,
                      tryFloat (tryGet "max_range_km" row),
                      tryFloat (tryGet "max_payload_kg" row),
                      tryFloat (tryGet "battery_capacity_wh" row),
                      tryFloat (tryGet "cruise_speed_ms" row) with
                | Some id, Some model, Some range, Some payload, Some battery, Some speed ->
                    Ok {
                        Id = id
                        Model = model
                        MaxRangeKm = range
                        MaxPayloadKg = payload
                        BatteryCapacityWh = battery
                        CruiseSpeedMs = speed
                    }
                | _ -> Error (sprintf "row=%d missing or invalid drone fields" rowNum))
            |> List.fold
                (fun (oks, errs) r ->
                    match r with
                    | Ok v -> (v :: oks, errs)
                    | Error e -> (oks, e :: errs))
                ([], [])
        
        (List.rev drones, structuralErrors @ (List.rev errors))

// =============================================================================
// PATH OPTIMIZATION
// =============================================================================

module PathOptimizer =
    
    /// Build distance matrix from waypoints (in kilometers)
    let buildDistanceMatrix (waypoints: Waypoint array) : float[,] =
        let n = waypoints.Length
        Array2D.init n n (fun i j ->
            if i = j then 0.0
            else Geography.distance3D waypoints.[i].Location waypoints.[j].Location)
    
    /// Convert waypoints to TSP city format
    let toTspCities (waypoints: Waypoint array) : (string * float * float) list =
        waypoints
        |> Array.map (fun wp -> (wp.Name, wp.Location.Latitude, wp.Location.Longitude))
        |> Array.toList
    
    /// Convert TSP solution to optimized route
    let toOptimizedRoute (drone: Drone) (waypoints: Waypoint array) (tour: int array) (totalDistanceKm: float) : OptimizedRoute =
        let orderedWaypoints = tour |> Array.map (fun i -> waypoints.[i]) |> Array.toList
        let flightTimeMin = (totalDistanceKm * 1000.0) / drone.CruiseSpeedMs / 60.0
        
        // Energy model based on domain constants
        // Battery.hoverPowerWPerKg (150 W/kg) is hover power; forward flight uses ~70% of that
        // Assuming typical drone mass of 2kg, forward flight power ≈ 150 * 2 * 0.7 = 210W
        // Energy = Power * Time, scaled by range fraction
        let energyWh = totalDistanceKm * Battery.hoverPowerWPerKg * Battery.forwardFlightEfficiencyFactor / drone.MaxRangeKm * drone.BatteryCapacityWh
        
        {
            DroneId = drone.Id
            Waypoints = orderedWaypoints
            TotalDistanceKm = totalDistanceKm
            EstimatedFlightTimeMin = flightTimeMin
            EnergyConsumptionWh = min energyWh drone.BatteryCapacityWh
        }
    
    /// Solve path planning using TSP.solve (quantum-first API with local simulation)
    let solveQuantum (waypoints: Waypoint array) : QuantumResult<TSP.Tour> =
        let cities = toTspCities waypoints
        TSP.solveDirectly cities None
    
    /// Solve path planning using Hybrid solver (auto-selects classical vs quantum)
    let solveHybrid (waypoints: Waypoint array) : QuantumResult<HybridSolver.Solution<FSharp.Azure.Quantum.Classical.TspSolver.TspSolution>> =
        let distances = buildDistanceMatrix waypoints
        HybridSolver.solveTsp distances None None None

// =============================================================================
// METRICS AND REPORTING
// =============================================================================

type Metrics = {
    run_id: string
    waypoints_path: string
    drones_path: string
    waypoints_sha256: string
    drones_sha256: string
    waypoint_count: int
    drone_count: int
    method_used: string
    total_distance_km: float
    estimated_flight_time_min: float
    energy_consumption_wh: float
    elapsed_ms: int64
}

// =============================================================================
// MAIN PROGRAM
// =============================================================================

module Program =
    
    let printRoute (route: OptimizedRoute) =
        printfn ""
        printfn "╔════════════════════════════════════════════════════════════╗"
        printfn "║  OPTIMIZED FLIGHT ROUTE                                    ║"
        printfn "╠════════════════════════════════════════════════════════════╣"
        printfn "║  Drone: %-50s ║" route.DroneId
        printfn "║  Total Distance: %8.2f km                               ║" route.TotalDistanceKm
        printfn "║  Flight Time: %8.1f min                                  ║" route.EstimatedFlightTimeMin
        printfn "║  Energy: %8.1f Wh                                        ║" route.EnergyConsumptionWh
        printfn "╠════════════════════════════════════════════════════════════╣"
        printfn "║  WAYPOINT SEQUENCE:                                        ║"
        route.Waypoints
        |> List.iteri (fun i wp ->
            let arrow = if i = 0 then "►" else "→"
            printfn "║  %s %2d. %-20s (%.4f, %.4f)         ║" arrow (i+1) wp.Name wp.Location.Latitude wp.Location.Longitude)
        printfn "╚════════════════════════════════════════════════════════════╝"
    
    [<EntryPoint>]
    let main argv =
        let args = Cli.parse argv
        
        if Cli.hasFlag "help" args || Cli.hasFlag "h" args then
            printfn "╔════════════════════════════════════════════════════════════╗"
            printfn "║  DRONE FLEET PATH PLANNING                                 ║"
            printfn "║  Quantum-Enhanced Route Optimization                       ║"
            printfn "╠════════════════════════════════════════════════════════════╣"
            printfn "║  Uses TSP solvers to find optimal waypoint visitation      ║"
            printfn "║  order, minimizing total flight distance.                  ║"
            printfn "╠════════════════════════════════════════════════════════════╣"
            printfn "║  OPTIONS:                                                  ║"
            printfn "║    --waypoints <path>  CSV file with waypoint coordinates  ║"
            printfn "║    --drones <path>     CSV file with drone specifications  ║"
            printfn "║    --out <dir>         Output directory for results        ║"
            printfn "║    --method <m>        classical | hybrid (default)        ║"
            printfn "║    --help              Show this help                      ║"
            printfn "╚════════════════════════════════════════════════════════════╝"
            0
        else
            let sw = Stopwatch.StartNew()
            
            let waypointsPath = Cli.getOr "waypoints" "examples/Drone/_data/waypoints.csv" args
            let dronesPath = Cli.getOr "drones" "examples/Drone/_data/drones.csv" args
            let outDir = Cli.getOr "out" (Path.Combine("runs", "drone", "fleet-path-planning")) args
            let method = Cli.getOr "method" "hybrid" args
            
            Data.ensureDirectory outDir
            let runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss")
            
            printfn ""
            printfn "╔════════════════════════════════════════════════════════════╗"
            printfn "║  DRONE FLEET PATH PLANNING                                 ║"
            printfn "║  FSharp.Azure.Quantum Example                              ║"
            printfn "╚════════════════════════════════════════════════════════════╝"
            printfn ""
            printfn "Loading waypoints from: %s" waypointsPath
            printfn "Loading drones from: %s" dronesPath
            printfn "Method: %s" method
            printfn ""
            
            // Read input data
            let waypoints, waypointErrors = Parse.readWaypoints waypointsPath
            let drones, droneErrors = Parse.readDrones dronesPath
            
            if not waypointErrors.IsEmpty then
                printfn "⚠ Waypoint parsing errors:"
                waypointErrors |> List.iter (printfn "  - %s")
            
            if not droneErrors.IsEmpty then
                printfn "⚠ Drone parsing errors:"
                droneErrors |> List.iter (printfn "  - %s")
            
            if waypoints.IsEmpty then
                printfn "❌ No waypoints loaded. Exiting."
                1
            else
                let waypointsArr = waypoints |> Array.ofList
                // Default drone specs - cruise speed within FAA Part 107 limit (Regulations.maxGroundSpeedMs = 44.7 m/s)
                let primaryDrone = drones |> List.tryHead |> Option.defaultValue {
                    Id = "DEFAULT"
                    Model = "Generic Multirotor"
                    MaxRangeKm = 20.0
                    MaxPayloadKg = 2.0
                    BatteryCapacityWh = 500.0
                    CruiseSpeedMs = 10.0  // ~36 km/h, well within Regulations.maxGroundSpeedMs
                }
                
                printfn "Optimizing route for %d waypoints..." waypoints.Length
                printfn ""
                
                let methodUsed, tour, totalDistance =
                    match method.ToLowerInvariant() with
                    | "quantum" ->
                        // Use TSP.solve directly (quantum-first API with local simulation)
                        match PathOptimizer.solveQuantum waypointsArr with
                        | Ok tourResult ->
                            // TSP.Tour returns city names, need to map back to indices
                            let nameToIndex = 
                                waypointsArr 
                                |> Array.mapi (fun i wp -> wp.Name, i) 
                                |> Map.ofArray
                            let tourIndices = 
                                tourResult.Cities 
                                |> List.choose (fun name -> Map.tryFind name nameToIndex)
                                |> Array.ofList
                            ("Quantum (QAOA via LocalBackend)", tourIndices, tourResult.TotalDistance)
                        | Error e ->
                            printfn "❌ Quantum solver failed: %s" e.Message
                            ("Failed", [||], 0.0)
                    | _ -> // hybrid (default)
                        match PathOptimizer.solveHybrid waypointsArr with
                        | Ok solution ->
                            let methodName =
                                match solution.Method with
                                | HybridSolver.SolverMethod.Classical -> "Hybrid → Classical"
                                | HybridSolver.SolverMethod.Quantum -> "Hybrid → Quantum (QAOA)"
                            (methodName, solution.Result.Tour, solution.Result.TourLength)
                        | Error e ->
                            printfn "❌ Hybrid solver failed: %s" e.Message
                            ("Failed", [||], 0.0)
                
                if tour.Length > 0 then
                    let route = PathOptimizer.toOptimizedRoute primaryDrone waypointsArr tour totalDistance
                    
                    printRoute route
                    
                    sw.Stop()
                    
                    // Write results
                    let waypointsSha = Data.fileSha256Hex waypointsPath
                    let dronesSha = Data.fileSha256Hex dronesPath
                    
                    let metrics: Metrics = {
                        run_id = runId
                        waypoints_path = waypointsPath
                        drones_path = dronesPath
                        waypoints_sha256 = waypointsSha
                        drones_sha256 = dronesSha
                        waypoint_count = waypoints.Length
                        drone_count = drones.Length
                        method_used = methodUsed
                        total_distance_km = totalDistance
                        estimated_flight_time_min = route.EstimatedFlightTimeMin
                        energy_consumption_wh = route.EnergyConsumptionWh
                        elapsed_ms = sw.ElapsedMilliseconds
                    }
                    
                    Reporting.writeJson (Path.Combine(outDir, "metrics.json")) metrics
                    
                    // Write route as CSV
                    let routeRows =
                        route.Waypoints
                        |> List.mapi (fun i wp ->
                            [ string (i + 1)
                              wp.Id
                              wp.Name
                              sprintf "%.6f" wp.Location.Latitude
                              sprintf "%.6f" wp.Location.Longitude
                              sprintf "%.1f" wp.Location.AltitudeMeters ])
                    
                    Reporting.writeCsv
                        (Path.Combine(outDir, "optimized_route.csv"))
                        [ "sequence"; "waypoint_id"; "name"; "latitude"; "longitude"; "altitude_m" ]
                        routeRows
                    
                    // Write report
                    let report = $"""# Drone Fleet Path Planning Results

## Summary

- **Run ID**: {runId}
- **Method**: {methodUsed}
- **Waypoints**: {waypoints.Length}
- **Total Distance**: {totalDistance:F2} km
- **Estimated Flight Time**: {route.EstimatedFlightTimeMin:F1} min
- **Energy Consumption**: {route.EnergyConsumptionWh:F1} Wh
- **Elapsed Time**: {sw.ElapsedMilliseconds} ms

## Optimized Route

| # | Waypoint | Name | Latitude | Longitude | Altitude (m) |
|---|----------|------|----------|-----------|--------------|
{route.Waypoints |> List.mapi (fun i wp -> sprintf "| %d | %s | %s | %.4f | %.4f | %.0f |" (i+1) wp.Id wp.Name wp.Location.Latitude wp.Location.Longitude wp.Location.AltitudeMeters) |> String.concat "\n"}

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
"""
                    
                    Reporting.writeTextFile (Path.Combine(outDir, "run-report.md")) report
                    
                    printfn ""
                    printfn "Results written to: %s" outDir
                    0
                else
                    printfn "❌ Optimization failed"
                    1
