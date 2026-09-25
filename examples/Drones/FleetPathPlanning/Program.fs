/// Drone Fleet Path Planning Example
///
/// This example demonstrates how to use FSharp.Azure.Quantum's TSP solvers
/// to optimize flight paths for a drone fleet visiting multiple waypoints.
///
/// DRONE DOMAIN MAPPING:
/// - Waypoints (delivery points, inspection sites) → TSP cities
/// - Drone flight distance → TSP edge weights
/// - Optimal visitation order → TSP tour
/// - Range-feasible split of that order → per-drone sorties (whole fleet used)
///
/// FLEET & CONSTRAINTS:
/// The optimizer finds one visitation order (base included, then rotated to
/// start there); FleetPlanner splits it into sorties from the base that each fit
/// the whole flight, out, legs and home again, into the drone's MaxRangeKm minus
/// the battery reserve. With `--needs-return false` the mission is one way and
/// aircraft end in an accepted termination zone. Waypoint altitudes are
/// checked against the AGL ceiling. This is a greedy decomposition, not a full
/// multi-depot vehicle-routing solver.
///
/// C2 RELAY MESH:
/// A 2.4 GHz link reaches about 2 km with margin. When the mission reaches
/// farther, the drone that lifts the most becomes a relay carrier: it flies
/// first and sets repeaters down on candidate sites (relay_sites.csv), each one
/// link from the base or a repeater already down, so every point of every
/// flown path stays in reach within Swarm.maxMeshHops hops.
/// No single repeater failure may cut anyone off (--minimal-relays drops that).
///
/// 1:N PERMISSION EVIDENCE:
/// permission-evidence.md / .json evidence deconfliction, endurance, C2 along
/// the tracks, altitude, and drop-outs (a mission aircraft, the carrier, a
/// repeater) for a one-pilot-to-many permission.
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
namespace FSharp.Azure.Quantum.Examples.Drones.FleetPathPlanning

open System
open System.Diagnostics
open System.Globalization
open System.IO

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend

open FSharp.Azure.Quantum.Examples.Common
open FSharp.Azure.Quantum.Examples.Drones.Domain

// =============================================================================
// DOMAIN TYPES
// =============================================================================

/// Geographic coordinate for a waypoint
type GeoCoordinate =
    {
        Latitude: float
        Longitude: float
        AltitudeMeters: float
    }

/// A waypoint in the drone mission
type Waypoint =
    {
        Id: string
        Name: string
        Location: GeoCoordinate
        Priority: int
    }

/// A drone in the fleet
type Drone =
    {
        Id: string
        Model: string
        MaxRangeKm: float
        MaxPayloadKg: float
        BatteryCapacityWh: float
        CruiseSpeedMs: float
    }

/// A place a C2 repeater can be set down (rooftop, hilltop, mast). The relay
/// carrier drops repeaters on the chosen sites before the mission launches.
type RelaySite =
    {
        Id: string
        Name: string
        Location: GeoCoordinate
    }

/// An area where the operator accepts an aircraft coming down (a field, a
/// quarry, open water): where one-way flights end, and where a fallback that
/// cannot get home goes instead of dropping wherever it is.
type TerminationZone =
    {
        Id: string
        Name: string
        /// Centre, at the height the aircraft arrives before its descent.
        Location: GeoCoordinate
        RadiusM: float
    }

/// Result of path optimization
type OptimizedRoute =
    {
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
            Math.Sin(dLat / 2.0) ** 2.0
            + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2.0) ** 2.0

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
            // Invariant: the CSVs use '.' decimals whatever the machine's locale.
            match Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture) with
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

                match
                    tryGet "waypoint_id" row,
                    tryGet "name" row,
                    tryFloat (tryGet "latitude" row),
                    tryFloat (tryGet "longitude" row),
                    tryFloat (tryGet "altitude_m" row),
                    tryInt (tryGet "priority" row)
                with
                | Some id, Some name, Some lat, Some lon, Some alt, Some pri ->
                    Ok
                        {
                            Id = id
                            Name = name
                            Location =
                                {
                                    Latitude = lat
                                    Longitude = lon
                                    AltitudeMeters = alt
                                }
                            Priority = pri
                        }
                | _ -> Error(sprintf "row=%d missing or invalid waypoint fields" rowNum))
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

                match
                    tryGet "drone_id" row,
                    tryGet "model" row,
                    tryFloat (tryGet "max_range_km" row),
                    tryFloat (tryGet "max_payload_kg" row),
                    tryFloat (tryGet "battery_capacity_wh" row),
                    tryFloat (tryGet "cruise_speed_ms" row)
                with
                // A drone that cannot move or has no range is not a drone: zero
                // speed makes every flight time infinite (the evidence sampling
                // never ends) and a negative one flies backwards through time.
                | Some id, Some model, Some range, Some payload, Some battery, Some speed when
                    speed > 0.0 && range > 0.0
                    ->
                    Ok
                        {
                            Id = id
                            Model = model
                            MaxRangeKm = range
                            MaxPayloadKg = payload
                            BatteryCapacityWh = battery
                            CruiseSpeedMs = speed
                        }
                | Some _, Some _, Some range, Some _, Some _, Some speed ->
                    Error(
                        sprintf "row=%d max_range_km (%g) and cruise_speed_ms (%g) must both be > 0" rowNum range speed
                    )
                | _ -> Error(sprintf "row=%d missing or invalid drone fields" rowNum))
            |> List.fold
                (fun (oks, errs) r ->
                    match r with
                    | Ok v -> (v :: oks, errs)
                    | Error e -> (oks, e :: errs))
                ([], [])

        (List.rev drones, structuralErrors @ (List.rev errors))

    let readTerminationZones (path: string) : TerminationZone list * string list =
        let rows, structuralErrors = Data.readCsvWithHeaderWithErrors path

        let zones =
            rows
            |> List.mapi (fun i row ->
                match
                    tryGet "zone_id" row,
                    tryGet "name" row,
                    tryFloat (tryGet "latitude" row),
                    tryFloat (tryGet "longitude" row),
                    tryFloat (tryGet "altitude_m" row),
                    tryFloat (tryGet "radius_m" row)
                with
                | Some id, Some name, Some lat, Some lon, Some alt, Some r when r > 0.0 ->
                    Ok
                        {
                            Id = id
                            Name = name
                            Location =
                                {
                                    Latitude = lat
                                    Longitude = lon
                                    AltitudeMeters = alt
                                }
                            RadiusM = r
                        }
                | _ -> Error(sprintf "row=%d missing or invalid termination zone fields" (i + 2)))

        (zones
         |> List.choose (function
             | Ok z -> Some z
             | Error _ -> None),
         structuralErrors
         @ (zones
            |> List.choose (function
                | Error e -> Some e
                | Ok _ -> None)))

    let readRelaySites (path: string) : RelaySite list * string list =
        let rows, structuralErrors = Data.readCsvWithHeaderWithErrors path

        let sites, errors =
            rows
            |> List.mapi (fun i row ->
                match
                    tryGet "site_id" row,
                    tryGet "name" row,
                    tryFloat (tryGet "latitude" row),
                    tryFloat (tryGet "longitude" row),
                    tryFloat (tryGet "altitude_m" row)
                with
                | Some id, Some name, Some lat, Some lon, Some alt ->
                    Ok
                        {
                            Id = id
                            Name = name
                            Location =
                                {
                                    Latitude = lat
                                    Longitude = lon
                                    AltitudeMeters = alt
                                }
                        }
                | _ -> Error(sprintf "row=%d missing or invalid relay site fields" (i + 2)))
            |> List.fold
                (fun (oks, errs) r ->
                    match r with
                    | Ok v -> (v :: oks, errs)
                    | Error e -> (oks, e :: errs))
                ([], [])

        (List.rev sites, structuralErrors @ (List.rev errors))

// =============================================================================
// PATH OPTIMIZATION
// =============================================================================

module PathOptimizer =

    /// Build distance matrix from waypoints (in kilometers)
    let buildDistanceMatrix (waypoints: Waypoint array) : float[,] =
        let n = waypoints.Length

        Array2D.init n n (fun i j ->
            if i = j then
                0.0
            else
                Geography.distance3D waypoints.[i].Location waypoints.[j].Location)

    /// Convert waypoints to TSP city format
    let toTspCities (waypoints: Waypoint array) : (string * float * float) list =
        waypoints
        |> Array.map (fun wp -> (wp.Name, wp.Location.Latitude, wp.Location.Longitude))
        |> Array.toList

    /// Convert TSP solution to optimized route
    let toOptimizedRoute
        (drone: Drone)
        (waypoints: Waypoint array)
        (tour: int array)
        (totalDistanceKm: float)
        : OptimizedRoute =
        let orderedWaypoints = tour |> Array.map (fun i -> waypoints.[i]) |> Array.toList
        let flightTimeMin = (totalDistanceKm * 1000.0) / drone.CruiseSpeedMs / 60.0

        // Energy model based on domain constants
        // Battery.hoverPowerWPerKg (150 W/kg) is hover power; forward flight uses ~70% of that
        // Assuming typical drone mass of 2kg, forward flight power ≈ 150 * 2 * 0.7 = 210W
        // Energy = Power * Time, scaled by range fraction
        let energyWh =
            totalDistanceKm
            * Battery.hoverPowerWPerKg
            * Battery.forwardFlightEfficiencyFactor
            / drone.MaxRangeKm
            * drone.BatteryCapacityWh

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
    let solveHybrid
        (waypoints: Waypoint array)
        : QuantumResult<HybridSolver.Solution<FSharp.Azure.Quantum.Classical.TspSolver.TspSolution>> =
        let distances = buildDistanceMatrix waypoints
        HybridSolver.solveTsp distances None None None

// =============================================================================
// FLEET DECOMPOSITION AND FEASIBILITY
// =============================================================================
//
// The TSP solver produces ONE optimal visitation order. A real fleet mission
// must then respect each vehicle's endurance. Every sortie launches from the
// base; what it must fit into is the whole flight: out to its first waypoint,
// the legs between waypoints, and home again when the drone is expected back,
// all inside MaxRangeKm minus the battery reserve. (A production planner would
// solve a full multi-depot VRP; this is a greedy split of one good order.)
//
// Returning is a mission decision, not a law of nature: when a drone is cheap
// and the mission critical, it can be flown one way and written off at its
// last waypoint (`--needs-return false`). The evidence pack then declares
// those planned losses and where they come down.

module FleetPlanner =

    /// One drone's assigned leg of the mission.
    type Sortie =
        {
            DroneId: string
            Model: string
            Waypoints: Waypoint list
            /// Flown distance: base -> waypoints, and -> base when it returns.
            DistanceKm: float
            /// Usable range: MaxRangeKm minus the battery reserve.
            RangeKm: float
            Returns: bool
            /// Where a one-way sortie ends; None when it returns, or when no
            /// termination zone was declared (the evidence fails that).
            Terminal: TerminationZone option
        }

    let usableKm (d: Drone) =
        d.MaxRangeKm * (1.0 - Battery.reserveBatteryPercent / 100.0)

    let zoneWaypoint (z: TerminationZone) : Waypoint =
        {
            Id = z.Id
            Name = z.Name
            Location = z.Location
            Priority = 0
        }

    /// The termination zone nearest a point.
    let nearestZone (zones: TerminationZone list) (at: GeoCoordinate) =
        zones
        |> List.sortBy (fun z -> Geography.haversineDistance at z.Location)
        |> List.tryHead

    /// A one-way sortie ends in the zone nearest its last waypoint.
    let terminalFor (returns: bool) (zones: TerminationZone list) (wps: Waypoint list) =
        match returns, wps with
        | false, _ :: _ -> nearestZone zones (List.last wps).Location
        | _ -> None

    /// The path a sortie flies: base, its waypoints, then base again if it
    /// returns, or its termination zone if it is flown one way.
    let path (baseWp: Waypoint) (returns: bool) (terminal: TerminationZone option) (wps: Waypoint list) =
        match wps with
        | [] -> []
        | _ ->
            baseWp :: wps
            @ (if returns then
                   [ baseWp ]
               else
                   terminal |> Option.map zoneWaypoint |> Option.toList)

    let sortiePath (baseWp: Waypoint) (s: Sortie) =
        path baseWp s.Returns s.Terminal s.Waypoints

    let pathKm (points: Waypoint list) =
        points
        |> List.pairwise
        |> List.sumBy (fun (a, b) -> Geography.distance3D a.Location b.Location)

    /// Split the ordered mission into sorties from the base, in fleet order. A
    /// sortie grows while its whole flight fits its drone's usable range; then
    /// the next drone that can reach the waypoint takes over. A waypoint no
    /// remaining drone can reach even on its own is reported as unassigned and
    /// planning carries on, instead of dropping the whole tail.
    let planSorties
        (baseWp: Waypoint)
        (needsReturn: bool)
        (zones: TerminationZone list)
        (drones: Drone array)
        (ordered: Waypoint array)
        : Sortie list * Waypoint list =
        let flown wps =
            pathKm (path baseWp needsReturn (terminalFor needsReturn zones wps) wps)

        let fits (d: Drone) wps = flown wps <= usableKm d

        let mk (d: Drone) (wps: Waypoint list) =
            {
                DroneId = d.Id
                Model = d.Model
                Waypoints = wps
                DistanceKm = flown wps
                RangeKm = usableKm d
                Returns = needsReturn
                Terminal = terminalFor needsReturn zones wps
            }

        let di, current, sorties, unassigned =
            ordered
            |> Array.fold
                (fun (di, current: Waypoint list, sorties, unassigned) w ->
                    if di >= drones.Length then
                        (di, current, sorties, w :: unassigned)
                    elif fits drones.[di] (List.rev (w :: current)) then
                        (di, w :: current, sorties, unassigned)
                    else
                        let sorties =
                            if current.IsEmpty then
                                sorties
                            else
                                mk drones.[di] (List.rev current) :: sorties

                        let from = if current.IsEmpty then di else di + 1

                        match [ from .. drones.Length - 1 ] |> List.tryFind (fun k -> fits drones.[k] [ w ]) with
                        | Some k -> (k, [ w ], sorties, unassigned)
                        | None -> (from, [], sorties, w :: unassigned))
                (0, [], [], [])

        let sorties =
            if current.IsEmpty then
                sorties
            else
                mk drones.[di] (List.rev current) :: sorties

        (List.rev sorties, List.rev unassigned)

    /// Waypoints whose altitude exceeds the regulatory AGL ceiling.
    let altitudeViolations (waypoints: Waypoint array) : Waypoint list =
        waypoints
        |> Array.filter (fun wp -> wp.Location.AltitudeMeters > Regulations.maxAltitudeAglMeters)
        |> Array.toList

// =============================================================================
// C2 RELAY MESH
// =============================================================================
//
// A 2.4 GHz link reaches about 2 km with a sensible fade margin, and the
// mission reaches farther. The usual fix is to set repeaters down first: one
// bigger drone (the relay carrier) flies out ahead of the mission and drops
// repeaters on chosen sites, each within one link of the base or of a repeater
// already placed, so the mesh grows outward from the base. Every aircraft is
// then in C2 range when it is within one link of any live node, through at
// most Swarm.maxMeshHops hops.
//
// Choosing the sites is a set-cover problem with a connectivity side
// condition. For a handful of candidate sites it is solved exactly by trying
// every subset, smallest first; beyond that a greedy cover is used.

module RelayMesh =

    module Ev = FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence

    /// One link's planning range (km): the radio and margin the evidence uses.
    let c2BandMhz = 2400.0
    let c2FadeMarginDb = 10.0
    let linkKm = Ev.C2.rangeKm c2BandMhz c2FadeMarginDb

    /// Sampling step along flown paths when checking coverage.
    let sampleKm = 0.1

    /// Repeaters chosen for a mission, in deployment order, with their hop
    /// count from the base (base = 0, a repeater linked to the base = 1).
    type Mesh =
        {
            Relays: (RelaySite * int) list
            /// Sampled mission points the mesh leaves out of reach.
            Uncovered: GeoCoordinate list
            /// Covered points that one repeater failure would cut off (only
            /// computed unless --minimal-relays).
            Fragile: GeoCoordinate list
            /// Deployment flight: base -> sites in order -> base.
            DeployKm: float
            /// True when the exhaustive search ran; false for the greedy fallback.
            Exact: bool
        }

    let private km (a: GeoCoordinate) (b: GeoCoordinate) = Geography.haversineDistance a b

    /// Hop count of every relay reachable from the base, level by level.
    let hops (baseLoc: GeoCoordinate) (relays: RelaySite list) : Map<string, int> =
        let rec bfs (frontier: (GeoCoordinate * int) list) (known: Map<string, int>) =
            let next =
                [
                    for loc, h in frontier do
                        for r in relays do
                            if not (known.ContainsKey r.Id) && km loc r.Location <= linkKm then
                                (r, h + 1)
                ]
                |> List.distinctBy (fun (r, _) -> r.Id)

            if next.IsEmpty then
                known
            else
                bfs
                    (next |> List.map (fun (r, h) -> (r.Location, h)))
                    (next |> List.fold (fun (m: Map<string, int>) (r, h) -> m.Add(r.Id, h)) known)

        bfs [ (baseLoc, 0) ] Map.empty

    /// Hops from the base to an aircraft at `p`: one more than the nearest
    /// (fewest-hop) node within one link; None when no node reaches it.
    let pointHops (baseLoc: GeoCoordinate) (relays: (RelaySite * int) list) (p: GeoCoordinate) =
        [
            if km baseLoc p <= linkKm then
                1
            for r, h in relays do
                if km r.Location p <= linkKm then
                    h + 1
        ]
        |> function
            | [] -> None
            | hs -> Some(List.min hs)

    let reachable baseLoc relays p =
        match pointHops baseLoc relays p with
        | Some h -> h <= Swarm.maxMeshHops
        | None -> false

    /// Points every `sampleKm` along a path (linear in lat/lon: fine over km).
    let sample (points: GeoCoordinate list) : GeoCoordinate list =
        let lerp (a: GeoCoordinate) (b: GeoCoordinate) f =
            {
                Latitude = a.Latitude + f * (b.Latitude - a.Latitude)
                Longitude = a.Longitude + f * (b.Longitude - a.Longitude)
                AltitudeMeters = a.AltitudeMeters + f * (b.AltitudeMeters - a.AltitudeMeters)
            }

        match points with
        | [] -> []
        | _ ->
            (points
             |> List.pairwise
             |> List.collect (fun (a, b) ->
                 let n = max 1 (int (Math.Ceiling(km a b / sampleKm)))
                 [ for i in 0 .. n - 1 -> lerp a b (float i / float n) ]))
            @ [ List.last points ]

    /// Deployment order: always fly next to the nearest site that is already
    /// linked (to the base or a placed repeater), so the carrier stays in the
    /// mesh it is building.
    let deployOrder (baseLoc: GeoCoordinate) (relays: RelaySite list) : RelaySite list =
        let rec go (at: GeoCoordinate) (placed: GeoCoordinate list) (left: RelaySite list) acc =
            let linked =
                left
                |> List.filter (fun r -> placed |> List.exists (fun p -> km p r.Location <= linkKm))

            match linked with
            | [] -> List.rev acc @ left // unreachable leftovers keep their order
            | _ ->
                let r = linked |> List.minBy (fun r -> km at r.Location)
                go r.Location (r.Location :: placed) (left |> List.filter (fun x -> x.Id <> r.Id)) (r :: acc)

        go baseLoc [ baseLoc ] relays []

    let deployKm (baseLoc: GeoCoordinate) (order: RelaySite list) =
        (baseLoc :: (order |> List.map (fun r -> r.Location)) @ [ baseLoc ])
        |> List.pairwise
        |> List.sumBy (fun (a, b) -> km a b)

    /// The relay carrier: the drone that lifts the most (ties: longest range).
    let pickCarrier (drones: Drone list) =
        drones
        |> List.sortByDescending (fun d -> (d.MaxPayloadKg, d.MaxRangeKm))
        |> List.tryHead

    let private connect baseLoc (subset: RelaySite list) =
        let h = hops baseLoc subset
        subset |> List.choose (fun r -> h.TryFind r.Id |> Option.map (fun k -> (r, k)))

    let private dark baseLoc (points: GeoCoordinate list) connected =
        points |> List.filter (reachable baseLoc connected >> not)

    type private Evaluation =
        {
            Connected: (RelaySite * int) list
            Uncovered: GeoCoordinate list
            /// Covered points that one repeater failure would cut off
            /// (only computed when redundancy is asked for).
            Fragile: GeoCoordinate list
        }

    /// Covered points that one repeater failure would cut off: one mesh
    /// recomputation per repeater, so by far the dearest part of an evaluation.
    let private fragileOf baseLoc (redundant: bool) (points: GeoCoordinate list) (subset: RelaySite list) connected =
        if not redundant then
            []
        else
            let covered = points |> List.filter (reachable baseLoc connected)

            subset
            |> List.collect (fun r ->
                dark baseLoc covered (connect baseLoc (subset |> List.filter (fun x -> x.Id <> r.Id))))
            |> List.distinct

    let private evaluate baseLoc (redundant: bool) (points: GeoCoordinate list) (subset: RelaySite list) =
        let connected = connect baseLoc subset

        {
            Connected = connected
            Uncovered = dark baseLoc points connected
            Fragile = fragileOf baseLoc redundant points subset connected
        }

    /// Above this many candidate sites the 2^n exhaustive search gives way to
    /// the greedy cover: 12 sites are 4095 subsets, each with up to 12 mesh
    /// recomputations for redundancy, which still runs in seconds.
    let exhaustiveLimit = 12

    let private mesh baseLoc exact (e: Evaluation) =
        let order = deployOrder baseLoc (e.Connected |> List.map fst)
        let hopOf = e.Connected |> List.map (fun (r, h) -> (r.Id, h)) |> Map.ofList

        {
            Relays = order |> List.map (fun r -> (r, hopOf.[r.Id]))
            Uncovered = e.Uncovered
            Fragile = e.Fragile
            DeployKm = deployKm baseLoc order
            Exact = exact
        }

    /// Repeater sites, chosen in strict order of importance: bring every
    /// sampled point into the mesh; then (with `redundant`) leave as few points
    /// as possible hanging on a single repeater; then use the fewest repeaters;
    /// then the shortest deployment flight. Coverage is never traded for
    /// redundancy.
    let choose
        (baseLoc: GeoCoordinate)
        (redundant: bool)
        (candidates: RelaySite list)
        (points: GeoCoordinate list)
        : Mesh =
        let none = evaluate baseLoc redundant points []

        let rank (s: RelaySite list) (e: Evaluation) =
            (e.Uncovered.Length,
             e.Fragile.Length,
             s.Length,
             deployKm baseLoc (deployOrder baseLoc (List.map fst e.Connected)))

        if none.Uncovered.IsEmpty then
            mesh baseLoc true none
        elif candidates.Length <= exhaustiveLimit then
            let cands = Array.ofList candidates

            // Subsets are generated lazily and only the best-ranked one is
            // kept, so memory stays flat however many there are. Ties keep the
            // first subset found, as a stable sort would.
            seq { 1 .. (1 <<< cands.Length) - 1 }
            |> Seq.map (fun m ->
                [
                    for i in 0 .. cands.Length - 1 do
                        if (m >>> i) &&& 1 = 1 then
                            cands.[i]
                ])
            |> Seq.fold
                (fun best s ->
                    let connected = connect baseLoc s

                    // Never deploy a repeater the mesh cannot link in.
                    if connected.Length <> s.Length then
                        best
                    else
                        let uncovered = dark baseLoc points connected

                        match best with
                        // Coverage ranks first: a subset leaving more points
                        // dark cannot win, so skip its redundancy work.
                        | Some((bestUncovered, _, _, _), _) when uncovered.Length > bestUncovered -> best
                        | _ ->
                            let e =
                                {
                                    Connected = connected
                                    Uncovered = uncovered
                                    Fragile = fragileOf baseLoc redundant points s connected
                                }

                            let r = rank s e

                            match best with
                            | Some(bestRank, _) when bestRank <= r -> best
                            | _ -> Some(r, e))
                None
            |> Option.map (snd >> mesh baseLoc true)
            |> Option.defaultValue (mesh baseLoc true none)
        else
            // Greedy fallback: keep adding the site that improves the rank most.
            let rec grow (chosen: RelaySite list) (current: Evaluation) =
                let options =
                    candidates
                    |> List.filter (fun c -> not (chosen |> List.exists (fun x -> x.Id = c.Id)))
                    |> List.map (fun c -> (c :: chosen, evaluate baseLoc redundant points (c :: chosen)))
                    |> List.filter (fun (s, e) -> e.Connected.Length = s.Length)

                let key (e: Evaluation) = (e.Uncovered.Length, e.Fragile.Length)

                match options |> List.sortBy (fun (s, e) -> rank s e) |> List.tryHead with
                | Some(s, e) when key e < key current -> grow s e
                | _ -> current

            mesh baseLoc false (grow [] none)

// =============================================================================
// 1:N PERMISSION EVIDENCE
// =============================================================================
//
// The pack reports on the plan as flown: the relay carrier's deployment flight
// first, then every sortie from the base. C2 is checked along the tracks, not
// only at waypoints: the union of link circles is not convex, so two covered
// waypoints can have a leg between them that leaves every circle.

module Evidence =

    module Ev = FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence

    /// Separation, coverage and airborne counts are sampled every second.
    let private stepS = 1.0

    /// Below this height an aircraft counts as on the ground.
    let private groundZ = 0.5

    type Settings =
        {
            /// The pilot station, and the centre of the ring of pads.
            Base: Waypoint
            /// Every aircraft's own launch and landing pad (see `pads`).
            Pads: Map<string, Ev.P3>
            /// Flights launch in order, this many seconds apart.
            LaunchIntervalS: float
            Pilots: int
            NeedsReturn: bool
            /// The relay carrier and the mesh it deploys; None when the base
            /// reaches the whole mission on its own.
            Relay: (Drone * RelayMesh.Mesh) option
            RelayMassKg: float
            /// Fallback (RTL) layer of the first sortie; each next sortie flies
            /// its fallback RtlStepM higher, so two fallbacks never share a level.
            RtlBaseM: float
            RtlStepM: float
            /// Where one-way flights end and stranded fallbacks go.
            Zones: TerminationZone list
        }

    /// A dropout is tried this often along every sortie.
    let private dropoutStepS = 5.0

    let private rtlAltitude (st: Settings) (sortieIndex: int) =
        st.RtlBaseM + float sortieIndex * st.RtlStepM

    /// Local metres around the base (equirectangular); the error is negligible
    /// over the few kilometres a sortie covers.
    let private toLocal (origin: GeoCoordinate) (p: GeoCoordinate) : Ev.P3 =
        let r = Environment.earthRadiusKm * 1000.0
        let cosLat0 = Math.Cos(Geography.toRadians origin.Latitude)

        {
            Ev.X = r * Geography.toRadians (p.Longitude - origin.Longitude) * cosLat0
            Ev.Y = r * Geography.toRadians (p.Latitude - origin.Latitude)
            Ev.Z = p.AltitudeMeters
        }

    /// Back from local metres to latitude/longitude (inverse of toLocal).
    let private geoOf (st: Settings) (p: Ev.P3) : GeoCoordinate =
        let r = Environment.earthRadiusKm * 1000.0
        let o = st.Base.Location
        let cosLat0 = Math.Cos(Geography.toRadians o.Latitude)

        {
            Latitude = o.Latitude + p.Y / r * 180.0 / Math.PI
            Longitude = o.Longitude + p.X / (r * cosLat0) * 180.0 / Math.PI
            AltitudeMeters = p.Z
        }

    /// Every aircraft on its own pad, on the ground, in a ring around the base.
    /// One shared launch point would put a landing aircraft onto others parked
    /// there waiting, and a fallback descending onto the base through anything
    /// climbing out; the ring is wide enough that neighbouring pads are at
    /// least two minimum separations apart.
    let padRingRadiusM (aircraft: int) =
        let spacing = 2.0 * Safety.minSwarmSeparationMeters
        max spacing (spacing / (2.0 * Math.Sin(Math.PI / float (max 2 aircraft))))

    let pads (fleet: Drone array) : Map<string, Ev.P3> =
        let radius = padRingRadiusM fleet.Length

        fleet
        |> Array.mapi (fun i d ->
            let angle = 2.0 * Math.PI * float i / float (max 1 fleet.Length)

            (d.Id,
             {
                 Ev.X = radius * Math.Cos angle
                 Ev.Y = radius * Math.Sin angle
                 Ev.Z = 0.0
             }))
        |> Map.ofArray

    /// A flight's first samples are its pad, then straight above it at the
    /// base's altitude; the planned path starts after them.
    let private launchSamples = 2

    /// Straight legs through `points` at `speed`, launching at `launchS` from
    /// the pad: the planned path's base points become a vertical climb from the
    /// pad and, when it lands back, a vertical descent onto it. No samples
    /// outside the flight, so an aircraft parked on its pad is not airborne
    /// (see `parked` for the separation check).
    let private fly
        (st: Settings)
        (droneId: string)
        (id: string)
        (speed: float)
        (landsAtPad: bool)
        (launchS: float)
        (points: GeoCoordinate list)
        =
        let pad = st.Pads.[droneId]
        let local = points |> List.map (toLocal st.Base.Location)
        let over (p: Ev.P3) = { pad with Z = p.Z }

        let local =
            match local with
            | [] -> []
            | first :: rest ->
                let rest =
                    match List.rev rest with
                    | last :: before when landsAtPad -> List.rev before @ [ over last; pad ]
                    | _ -> rest

                pad :: over first :: rest
            |> Array.ofList

        let times =
            local
            |> Array.pairwise
            |> Array.scan (fun t (a, b) -> t + Ev.dist3 a b / speed) launchS

        {
            Ev.AircraftId = id
            Ev.Samples = Array.zip times local
        }

    /// The same track with the aircraft parked on the ground before launch
    /// (from the start of the operation) and after landing (until `horizonS`),
    /// so anything flying low past a pad is compared with the aircraft waiting
    /// on it. For separation only: parked is not airborne.
    let private parked (horizonS: float) (tr: Ev.Track) =
        match tr.Samples with
        | [||] -> tr
        | s ->
            let t0, p0 = s.[0]
            let t1, p1 = Array.last s

            { tr with
                Samples =
                    Array.concat
                        [
                            (if p0.Z < groundZ && t0 > 0.0 then [| (0.0, p0) |] else [||])
                            s
                            (if p1.Z < groundZ && t1 < horizonS then
                                 [| (horizonS, p1) |]
                             else
                                 [||])
                        ]
            }

    /// Closest approach over flight tracks, each parked on its pad around its
    /// flight until the last of them ends.
    let private closestWithParked (tracks: Ev.Track list) =
        let horizon =
            tracks
            |> List.filter (fun t -> t.Samples.Length > 0)
            |> List.map (fun t -> fst (Array.last t.Samples))
            |> List.fold max 0.0

        Ev.closestApproach stepS groundZ (tracks |> List.map (parked horizon))

    /// The relay carrier's flight, and the moment each repeater goes live.
    let private carrierFlight (st: Settings) =
        st.Relay
        |> Option.map (fun (carrier, mesh) ->
            let sites = mesh.Relays |> List.map fst

            let track =
                st.Base.Location :: (sites |> List.map (fun r -> r.Location))
                @ [ st.Base.Location ]
                |> fly st carrier.Id (carrier.Id + " (relay carrier)") carrier.CruiseSpeedMs true 0.0

            (track, sites |> List.mapi (fun i r -> (r, fst track.Samples.[i + launchSamples]))))

    /// The mission launches once the carrier is back and the mesh is complete.
    let private missionStart (st: Settings) =
        match carrierFlight st with
        | Some(track, _) -> fst (Array.last track.Samples) + st.LaunchIntervalS
        | None -> 0.0

    let private sortieTracks (st: Settings) (droneById: Map<string, Drone>) (sorties: FleetPlanner.Sortie list) =
        let t0 = missionStart st

        sorties
        |> List.mapi (fun i s ->
            FleetPlanner.sortiePath st.Base s
            |> List.map (fun w -> w.Location)
            |> fly
                st
                s.DroneId
                s.DroneId
                droneById.[s.DroneId].CruiseSpeedMs
                s.Returns
                (t0 + float i * st.LaunchIntervalS))

    /// The fallback of an aircraft leaving the plan at `t` from `p`: straight
    /// up or down to its own RTL layer, straight to `target` at that layer,
    /// down onto it.
    let private fallbackTo (target: Ev.P3) (speed: float) (rtlAltM: float) (id: string) (t: float) (p: Ev.P3) =
        let points = [| p; { p with Z = rtlAltM }; { target with Z = rtlAltM }; target |]

        {
            Ev.AircraftId = id
            Ev.Samples =
                Array.zip
                    (points
                     |> Array.pairwise
                     |> Array.scan (fun acc (a, b) -> acc + Ev.dist3 a b / speed) t)
                    points
        }

    /// Home is the aircraft's own pad, never the shared base point.
    let private fallback
        (st: Settings)
        (droneId: string)
        (speed: float)
        (rtlAltM: float)
        (id: string)
        (t: float)
        (p: Ev.P3)
        =
        fallbackTo st.Pads.[droneId] speed rtlAltM id t p

    /// Controlled descent in place (Safety.imuFailureDescentRateMs), for an
    /// aircraft that cannot get home.
    let private fallbackDescent (st: Settings) (id: string) (t: float) (p: Ev.P3) =
        {
            Ev.AircraftId = id
            Ev.Samples = [| (t, p); (t + p.Z / Safety.imuFailureDescentRateMs, { p with Z = 0.0 }) |]
        }

    let private pathLengthKm (tr: Ev.Track) =
        tr.Samples
        |> Array.pairwise
        |> Array.sumBy (fun ((_, a), (_, b)) -> Ev.dist3 a b)
        |> fun m -> m / 1000.0

    type private C2Sample =
        {
            Aircraft: string
            TimeS: float
            Hops: int option
            NearestKm: float
        }

    /// Every airborne second of every track: which live node (the base, or a
    /// repeater already set down) reaches the aircraft, in how many hops.
    let private c2Samples (st: Settings) (tracks: Ev.Track list) (live: (RelaySite * float) list) =
        let baseP = toLocal st.Base.Location st.Base.Location

        let hopOf =
            st.Relay
            |> Option.map (fun (_, m) -> Map.ofList [ for r, h in m.Relays -> (r.Id, h) ])

        let nodes =
            live
            |> List.map (fun (r, at) -> (toLocal st.Base.Location r.Location, at, hopOf.Value.[r.Id]))

        let horizKm (a: Ev.P3) (b: Ev.P3) =
            Math.Sqrt((a.X - b.X) ** 2.0 + (a.Y - b.Y) ** 2.0) / 1000.0

        [
            for tr in tracks do
                for t in fst tr.Samples.[0] .. stepS .. fst (Array.last tr.Samples) do
                    match Ev.positionAt tr t with
                    | Some p ->
                        let liveNodes =
                            (baseP, 0)
                            :: [
                                for q, at, h in nodes do
                                    if at <= t then
                                        (q, h)
                            ]

                        let inReach =
                            liveNodes |> List.filter (fun (q, _) -> horizKm q p <= RelayMesh.linkKm)

                        {
                            Aircraft = tr.AircraftId
                            TimeS = t
                            Hops =
                                if inReach.IsEmpty then
                                    None
                                else
                                    Some(1 + (inReach |> List.map snd |> List.min))
                            NearestKm = liveNodes |> List.map (fun (q, _) -> horizKm q p) |> List.min
                        }
                    | None -> ()
        ]

    let private label (w: Waypoint) = sprintf "%s %s" w.Id w.Name

    /// Sampled mission points (from the flown paths) the mesh does not reach.
    let private outOfMesh (st: Settings) (relays: (RelaySite * int) list) (sorties: FleetPlanner.Sortie list) =
        sorties
        |> List.collect (fun s -> FleetPlanner.sortiePath st.Base s |> List.map (fun w -> w.Location))
        |> RelayMesh.sample
        |> List.filter (RelayMesh.reachable st.Base.Location relays >> not)

    let build
        (st: Settings)
        (fleet: Drone array)
        (missionFleet: Drone array)
        (ordered: Waypoint array)
        (sorties: FleetPlanner.Sortie list)
        (unassigned: Waypoint list)
        : Ev.Pack =
        let droneById = fleet |> Array.map (fun d -> d.Id, d) |> Map.ofArray
        let carrier = carrierFlight st

        let tracks =
            (carrier |> Option.map fst |> Option.toList) @ sortieTracks st droneById sorties

        let flown =
            sorties
            |> List.collect (fun s -> s.Waypoints)
            |> List.distinctBy (fun w -> w.Id)

        let relays =
            st.Relay |> Option.map (fun (_, m) -> m.Relays) |> Option.defaultValue []

        // --- Coverage of the mission itself ----------------------------------
        let coverage =
            {
                Area = Ev.Deconfliction
                Claim = "The plan covers every mission waypoint"
                Method = "waypoints the planner could not fit into any drone's usable range"
                Measured = sprintf "%d of %d waypoints assigned" (ordered.Length - unassigned.Length) ordered.Length
                Limit = "all assigned"
                Status = if unassigned.IsEmpty then Ev.Pass else Ev.Fail
                Details = unassigned |> List.map (fun w -> sprintf "%s: no drone reaches it" (label w))
            }
            : Ev.Check

        // --- Deconfliction --------------------------------------------------
        let separation =
            closestWithParked tracks
            |> Ev.Checks.separation
                Safety.minSwarmSeparationMeters
                (sprintf
                    "relay carrier first, then every sortie, launched %.0f s apart, each from its own pad on a %.0f m ring around the base: vertical climb to the base's altitude, straight legs at its drone's cruise speed, vertical descent onto the pad; aircraft parked on their pads before launch and after landing included; exact closest approach of every pair not both on the ground"
                    st.LaunchIntervalS
                    (padRingRadiusM st.Pads.Count))

        // --- Endurance ------------------------------------------------------
        let endurance =
            let legs =
                (sorties |> List.map (fun s -> (s.DroneId, s.DistanceKm, s.RangeKm)))
                @ (match st.Relay with
                   | Some(c, mesh) -> [ (c.Id + " (relay carrier)", mesh.DeployKm, FleetPlanner.usableKm c) ]
                   | None -> [])

            let check =
                legs
                |> Ev.Checks.endurance
                    "km"
                    (sprintf
                        "per flight, path base -> waypoints%s vs. MaxRangeKm x (1 - %.0f%% reserve); the relay carrier always returns"
                        (if st.NeedsReturn then
                             " -> base"
                         else
                             " (one way: expended at the last waypoint)")
                        Battery.reserveBatteryPercent)

            { check with
                Details =
                    check.Details
                    @ (legs
                       |> List.map (fun (id, need, have) -> sprintf "%s: %.2f of %.2f km usable" id need have))
            }

        // --- C2 link --------------------------------------------------------
        let samples =
            c2Samples st tracks (carrier |> Option.map snd |> Option.defaultValue [])

        let outOfReach =
            samples
            |> List.filter (fun s ->
                match s.Hops with
                | Some h -> h > Swarm.maxMeshHops
                | None -> true)

        let c2 =
            let worst =
                samples
                |> List.sortByDescending (fun s -> s.NearestKm)
                |> List.tryHead
                |> Option.defaultValue
                    {
                        Aircraft = "-"
                        TimeS = 0.0
                        Hops = Some 0
                        NearestKm = 0.0
                    }

            let maxHops = samples |> List.choose (fun s -> s.Hops) |> List.fold max 0

            {
                Area = Ev.C2Link
                Claim =
                    sprintf
                        "Every aircraft stays in C2 reach of the pilot station at %s, directly or through the repeater mesh"
                        (label st.Base)
                Method =
                    sprintf
                        "every airborne second of every flight: horizontal distance to the nearest live node (the base, or a repeater already set down), one link = %s; hops through the mesh"
                        (Ev.C2.describe RelayMesh.c2BandMhz RelayMesh.c2FadeMarginDb)
                Measured =
                    sprintf
                        "farthest from a live node %.2f km (%s, t=%.0f s); up to %d hop(s); %d of %d samples out of reach"
                        worst.NearestKm
                        worst.Aircraft
                        worst.TimeS
                        maxHops
                        outOfReach.Length
                        samples.Length
                Limit = sprintf "<= %.2f km per link, <= %d hops" RelayMesh.linkKm Swarm.maxMeshHops
                Status = if outOfReach.IsEmpty then Ev.Pass else Ev.Fail
                Details =
                    (relays
                     |> List.map (fun (r, h) -> sprintf "repeater %s %s: %d hop(s) from the base" r.Id r.Name h))
                    @ (outOfReach
                       |> List.groupBy (fun s -> s.Aircraft)
                       |> List.map (fun (a, ss) ->
                           sprintf
                               "%s out of reach for %d s from t=%.0f s (up to %.2f km from a live node)"
                               a
                               ss.Length
                               (ss |> List.map (fun s -> s.TimeS) |> List.min)
                               (ss |> List.map (fun s -> s.NearestKm) |> List.max)))
            }
            : Ev.Check

        let carrierLift =
            match st.Relay with
            // No mesh: that is only fine when the C2 samples say the base alone
            // reaches every flight, zone legs and fallbacks' tracks included.
            | None ->
                Ev.Checks.contingency
                    "No repeaters are needed"
                    "every airborne second of every flight within one link of the base (the C2 samples above)"
                    (if outOfReach.IsEmpty then
                         "base reaches the whole mission"
                     else
                         sprintf
                             "%d of %d C2 samples out of reach and no repeater mesh deployed"
                             outOfReach.Length
                             samples.Length)
                    (if outOfReach.IsEmpty then Ev.Pass else Ev.Fail)
                    (if outOfReach.IsEmpty then
                         []
                     else
                         [
                             "the flown paths leave one link of the base, but no relay carrier and repeaters were planned: declare candidate sites with --relays, and a drone to carry them"
                         ])
                |> fun c -> { c with Area = Ev.C2Link }
            | Some(c, mesh) ->
                let load = float mesh.Relays.Length * st.RelayMassKg

                {
                    Area = Ev.C2Link
                    Claim = "The relay carrier can lift every repeater"
                    Method =
                        sprintf "%d repeater(s) x %.2f kg vs. %s MaxPayloadKg" mesh.Relays.Length st.RelayMassKg c.Id
                    Measured = sprintf "%.2f kg of %.2f kg" load c.MaxPayloadKg
                    Limit = "load <= payload"
                    Status = if load <= c.MaxPayloadKg then Ev.Pass else Ev.Fail
                    Details =
                        [
                            sprintf
                                "sites chosen %s (%s)"
                                (if mesh.Exact then
                                     "exactly: fewest repeaters, then shortest deployment flight"
                                 else
                                     "greedily")
                                (mesh.Relays |> List.map (fun (r, _) -> r.Id) |> String.concat " -> ")
                        ]
                }

        // --- Altitude -------------------------------------------------------
        let altitude =
            (st.Base :: flown
             |> List.distinctBy (fun w -> w.Id)
             |> List.map (fun w -> (label w, w.Location.AltitudeMeters)))
            @ (relays
               |> List.map (fun (r, _) -> (sprintf "repeater %s %s" r.Id r.Name, r.Location.AltitudeMeters)))
            @ (sorties
               |> List.mapi (fun k s -> (sprintf "%s fallback layer" s.DroneId, rtlAltitude st k)))
            |> Ev.Checks.altitude

        // --- Contingency ----------------------------------------------------
        let alreadyUnassigned = unassigned |> List.map (fun w -> w.Id) |> Set.ofList

        let losses =
            missionFleet
            |> Array.mapi (fun k lost ->
                let rest = missionFleet |> Array.removeAt k

                let replanned, left =
                    FleetPlanner.planSorties st.Base st.NeedsReturn st.Zones rest ordered

                let newlyLost = left |> List.filter (fun w -> not (alreadyUnassigned.Contains w.Id))
                let short = replanned |> List.filter (fun s -> s.DistanceKm > s.RangeKm)
                let dark = outOfMesh st relays replanned

                let closest =
                    closestWithParked (
                        (carrier |> Option.map fst |> Option.toList)
                        @ sortieTracks st droneById replanned
                    )

                let separated =
                    closest
                    |> Option.forall (fun c -> c.Distance >= Safety.minSwarmSeparationMeters)

                let ok = newlyLost.IsEmpty && short.IsEmpty && dark.IsEmpty && separated

                let detail =
                    sprintf
                        "without %s: %d sortie(s); %s; %s; %s; %s"
                        lost.Id
                        replanned.Length
                        (if newlyLost.IsEmpty then
                             "full coverage"
                         else
                             sprintf
                                 "%d waypoint(s) unassigned (%s)"
                                 newlyLost.Length
                                 (newlyLost |> List.map (fun w -> w.Id) |> String.concat ", "))
                        (if short.IsEmpty then
                             "endurance ok"
                         else
                             sprintf "%d sortie(s) short of range" short.Length)
                        (if dark.IsEmpty then
                             "C2 ok"
                         else
                             sprintf "%d sampled point(s) out of C2 reach" dark.Length)
                        (match closest with
                         | Some c -> sprintf "closest approach %.1f m" c.Distance
                         | None -> "fewer than two airborne together")

                (ok, detail))
            |> List.ofArray

        let replan =
            let held = losses |> List.filter fst |> List.length

            Ev.Checks.contingency
                "Losing any one mission aircraft before launch, the rest still cover every waypoint within endurance, C2 and separation"
                "for each mission aircraft: remove it, re-plan the same waypoint order with the rest, then re-check coverage, endurance with reserve, C2 through the deployed mesh, and separation"
                (sprintf "%d of %d single-aircraft losses hold" held losses.Length)
                (if held = losses.Length then Ev.Pass else Ev.Fail)
                (losses |> List.map snd)

        // A repeater that fails leaves whatever only it reached out of C2.
        let relayLoss =
            match relays with
            | [] -> []
            | _ ->
                let cuts =
                    relays
                    |> List.map (fun (r, _) ->
                        let rest = relays |> List.map fst |> List.filter (fun x -> x.Id <> r.Id)
                        let h = RelayMesh.hops st.Base.Location rest

                        let still =
                            rest |> List.choose (fun x -> h.TryFind x.Id |> Option.map (fun k -> (x, k)))

                        (r, outOfMesh st still sorties))

                let single = cuts |> List.filter (fun (_, dark) -> not dark.IsEmpty)

                [
                    Ev.Checks.contingency
                        "Losing any one repeater leaves every aircraft in C2 reach"
                        "for each repeater: remove it, recompute the mesh, re-check every sampled point of every flown path"
                        (sprintf "%d of %d repeaters are single points of failure" single.Length relays.Length)
                        (if single.IsEmpty then Ev.Pass else Ev.Fail)
                        (single
                         |> List.map (fun (r, dark) ->
                             sprintf
                                 "without %s %s: %d sampled point(s) lose C2; aircraft there fall back to the lost-link procedure (add a candidate site, or drop --minimal-relays)"
                                 r.Id
                                 r.Name
                                 dark.Length))
                ]

        let carrierLoss =
            match st.Relay with
            | None -> []
            | Some(c, mesh) ->
                let others = missionFleet |> List.ofArray
                let load = float mesh.Relays.Length * st.RelayMassKg

                let spare =
                    RelayMesh.pickCarrier (
                        others
                        |> List.filter (fun d -> d.MaxPayloadKg >= load && FleetPlanner.usableKm d >= mesh.DeployKm)
                    )

                [
                    Ev.Checks.contingency
                        (sprintf "Losing the relay carrier %s before the mesh is complete" c.Id)
                        "another drone that can lift the repeaters and fly the deployment with reserve; the mission does not launch until the mesh is complete, so a later loss changes nothing"
                        (match spare with
                         | Some d ->
                             sprintf
                                 "%s can take over (%.1f kg payload, %.1f km usable)"
                                 d.Id
                                 d.MaxPayloadKg
                                 (FleetPlanner.usableKm d)
                         | None -> "no other drone can deploy the mesh")
                        (if spare.IsSome then Ev.Pass else Ev.Fail)
                        [
                            if spare.IsNone then
                                sprintf "needs %.2f kg payload and %.2f km usable range" load mesh.DeployKm
                            "the mission then flies one aircraft short: see the single-loss check above"
                        ]
                ]

        // One-way aircraft end in a termination zone: they fly to its centre and
        // descend under control; the descent drifts downwind, so the zone must
        // hold the whole drift at the strongest wind the operation flies in.
        let expended =
            if st.NeedsReturn then
                []
            else
                let drift (z: TerminationZone) =
                    z.Location.AltitudeMeters / Safety.imuFailureDescentRateMs
                    * Safety.maxOperatingWindSpeedMs

                let ends =
                    sorties
                    |> List.map (fun s ->
                        match s.Terminal with
                        | Some z -> (s, Some z, drift z <= z.RadiusM)
                        | None -> (s, None, false))

                [
                    Ev.Checks.contingency
                        "Aircraft expended by design come down inside a termination zone the operator accepts"
                        (sprintf
                            "each one-way sortie ends with a leg to the zone nearest its last waypoint (inside its usable range), then a controlled descent at %.1f m/s; drift = arrival height / descent rate x %.0f m/s wind, which must stay inside the zone radius"
                            Safety.imuFailureDescentRateMs
                            Safety.maxOperatingWindSpeedMs)
                        (sprintf
                            "%d of %d one-way sortie(s) end inside a zone"
                            (ends |> List.filter (fun (_, _, ok) -> ok) |> List.length)
                            ends.Length)
                        (if ends |> List.forall (fun (_, _, ok) -> ok) then
                             Ev.Pass
                         else
                             Ev.Fail)
                        (ends
                         |> List.map (fun (s, z, ok) ->
                             match z with
                             | Some z ->
                                 sprintf
                                     "%s ends in %s %s: drift %.0f m of %.0f m radius%s"
                                     s.DroneId
                                     z.Id
                                     z.Name
                                     (drift z)
                                     z.RadiusM
                                     (if ok then "" else "  <-- FAIL")
                             | None -> sprintf "%s: no termination zone declared (--termination-zones)" s.DroneId))
                ]

        // An aircraft dropping out mid-flight (lost link, low battery, a fault)
        // is expected, not exceptional. What must hold is that the swarm absorbs
        // it: the others fly on untouched, because the dropped aircraft's
        // fallback never comes near them (if it did they would have to react,
        // and that is how failures cascade), and its unflown waypoints fit a
        // follow-up sortie by the rest of the fleet.
        let dropouts =
            let missionTracks = sortieTracks st droneById sorties

            let others (id: string) =
                tracks |> List.filter (fun t -> t.AircraftId <> id)

            let tracksEndS =
                tracks |> List.map (fun t -> fst (Array.last t.Samples)) |> List.fold max 0.0

            List.zip sorties missionTracks
            |> List.mapi (fun k (s, tr) ->
                let d = droneById.[s.DroneId]
                let rtlAlt = rtlAltitude st k
                let launch = fst tr.Samples.[0]
                let landing = fst (Array.last tr.Samples)
                // Times the aircraft passes each waypoint (after the climb).
                let reached =
                    s.Waypoints |> List.mapi (fun i w -> (w, fst tr.Samples.[i + launchSamples]))

                [ launch + dropoutStepS .. dropoutStepS .. landing - 1.0 ]
                |> List.choose (fun t ->
                    Ev.positionAt tr t
                    |> Option.map (fun p ->
                        let home =
                            fallback st s.DroneId d.CruiseSpeedMs rtlAlt (s.DroneId + " fallback") t p

                        let flownKm = d.CruiseSpeedMs * (t - launch) / 1000.0
                        let homeKm = pathLengthKm home
                        let goesHome = flownKm + homeKm <= FleetPlanner.usableKm d

                        // Out of battery to get home: the nearest termination
                        // zone if it can reach one, else a controlled descent
                        // in place.
                        let toZone =
                            FleetPlanner.nearestZone st.Zones (geoOf st p)
                            |> Option.map (fun z ->
                                let target = toLocal st.Base.Location z.Location
                                fallbackTo target d.CruiseSpeedMs rtlAlt (s.DroneId + " to " + z.Id) t p)
                            |> Option.filter (fun tr -> flownKm + pathLengthKm tr <= FleetPlanner.usableKm d)

                        let fb =
                            if goesHome then
                                home
                            else
                                toZone
                                |> Option.defaultWith (fun () -> fallbackDescent st (s.DroneId + " descent") t p)

                        // Everyone else parked on their pads too, until the
                        // fallback or the last flight has ended.
                        let horizon = max tracksEndS (fst (Array.last fb.Samples))

                        let closest =
                            others s.DroneId
                            |> List.choose (fun o ->
                                Ev.closestApproach stepS groundZ [ parked horizon fb; parked horizon o ])
                            |> List.sortBy (fun c -> c.Distance)
                            |> List.tryHead

                        let leftovers = reached |> List.filter (fun (_, at) -> at > t) |> List.map fst

                        let absorbedBy =
                            if leftovers.IsEmpty then
                                Some []
                            else
                                let rest = missionFleet |> Array.filter (fun x -> x.Id <> s.DroneId)

                                let followUp, left =
                                    FleetPlanner.planSorties
                                        st.Base
                                        st.NeedsReturn
                                        st.Zones
                                        rest
                                        (Array.ofList leftovers)

                                if left.IsEmpty then
                                    Some(followUp |> List.map (fun f -> f.DroneId))
                                else
                                    None

                        {|
                            Aircraft = s.DroneId
                            TimeS = t
                            GoesHome = goesHome
                            Closest = closest
                            Leftovers = leftovers.Length
                            AbsorbedBy = absorbedBy
                            FallbackEndS = fst (Array.last fb.Samples)
                        |})))

        let dropoutCheck =
            let all = dropouts |> List.concat

            let conflicts =
                all
                |> List.filter (fun x ->
                    x.Closest
                    |> Option.exists (fun c -> c.Distance < Safety.minSwarmSeparationMeters))

            let unabsorbed = all |> List.filter (fun x -> x.AbsorbedBy.IsNone)

            let worst =
                all
                |> List.choose (fun x -> x.Closest |> Option.map (fun c -> (x, c)))
                |> List.sortBy (fun (_, c) -> c.Distance)
                |> List.tryHead

            Ev.Checks.contingency
                "Any aircraft can drop out at any moment and the swarm absorbs it: no knock-on conflict, and its unflown waypoints are re-flown"
                (sprintf
                    "every %.0f s along every sortie: the aircraft leaves the plan and flies its fallback (to its own RTL layer, straight over its own pad, down onto it; or a controlled descent in place when the battery cannot get it home) while everyone else flies on unchanged; closest approach of the fallback to every other aircraft, and whether its unflown waypoints fit a follow-up sortie by the rest of the fleet after a battery swap"
                    dropoutStepS)
                (sprintf
                    "%d dropout moments; closest fallback approach %s; %d knock-on conflict(s); %d with waypoints left unabsorbed"
                    all.Length
                    (match worst with
                     | Some(x, c) -> sprintf "%.1f m (%s dropping at t=%.0f s, vs %s)" c.Distance x.Aircraft x.TimeS c.B
                     | None -> "n/a (no other aircraft airborne)")
                    conflicts.Length
                    unabsorbed.Length)
                (if all.IsEmpty || (conflicts.IsEmpty && unabsorbed.IsEmpty) then
                     Ev.Pass
                 else
                     Ev.Fail)
                ((dropouts
                  |> List.choose (fun xs ->
                      match xs with
                      | [] -> None
                      | x :: _ ->
                          let homes = xs |> List.filter (fun y -> y.GoesHome) |> List.length

                          let near =
                              xs
                              |> List.choose (fun y -> y.Closest)
                              |> List.sortBy (fun c -> c.Distance)
                              |> List.tryHead

                          let absorbers =
                              xs |> List.choose (fun y -> y.AbsorbedBy) |> List.concat |> List.distinct

                          Some(
                              sprintf
                                  "%s: %d dropout moments, fallback home in %d (descent in place otherwise); nearest other aircraft %s; leftovers re-flown by %s%s"
                                  x.Aircraft
                                  xs.Length
                                  homes
                                  (near
                                   |> Option.map (fun c -> sprintf "%.0f m" c.Distance)
                                   |> Option.defaultValue "none airborne")
                                  (if absorbers.IsEmpty then
                                       "-"
                                   else
                                       String.Join(", ", absorbers))
                                  (let lost = xs |> List.filter (fun y -> y.AbsorbedBy.IsNone) |> List.length

                                   if lost = 0 then
                                       ""
                                   else
                                       sprintf "; %d moment(s) leave waypoints nobody can re-fly" lost)
                          )))
                 @ (conflicts
                    |> List.truncate 5
                    |> List.map (fun x ->
                        let c = x.Closest.Value

                        sprintf
                            "conflict: %s dropping at t=%.0f s passes %.1f m from %s"
                            x.Aircraft
                            x.TimeS
                            c.Distance
                            c.B)))

        // --- Supervisor workload ---------------------------------------------
        let peak = Ev.peakAirborne stepS tracks

        let event name aircraft start duration handling response =
            {
                Ev.Event = name
                Ev.Affected = aircraft
                Ev.StartS = start
                Ev.DurationS = duration
                Ev.Handling = handling
                Ev.Response = response
            }

        // Every in-flight dropout: the fallback flies itself; a knock-on
        // conflict or a follow-up sortie needs the pilot.
        let dropoutScenarios =
            dropouts
            |> List.concat
            |> List.map (fun x ->
                let name = sprintf "%s drops out at t=%.0f s" x.Aircraft x.TimeS

                (name,
                 [
                     event
                         name
                         1
                         x.TimeS
                         (x.FallbackEndS - x.TimeS)
                         Ev.Automatic
                         (if x.GoesHome then
                              "fallback: own RTL layer, straight to its own pad"
                          else
                              "controlled descent in place")
                     match x.Closest with
                     | Some c when c.Distance < Safety.minSwarmSeparationMeters ->
                         event
                             (sprintf "%s passes %.1f m from %s" x.Aircraft c.Distance c.B)
                             1
                             x.TimeS
                             Ev.decisionTimeS
                             Ev.PilotDecision
                             (sprintf "divert %s" c.B)
                     | _ -> ()
                     if x.Leftovers > 0 then
                         event
                             (sprintf "%d waypoint(s) unflown" x.Leftovers)
                             0
                             x.FallbackEndS
                             Ev.decisionTimeS
                             Ev.PilotDecision
                             (match x.AbsorbedBy with
                              | Some by -> sprintf "approve the follow-up sortie (%s)" (String.Join(", ", by))
                              | None -> "decide what the mission drops: no drone can re-fly them")
                 ]))

        // A repeater failure sends every sortie that relies on it into its
        // lost-link fallback: the aircraft count is the sorties that enter the
        // area it alone covered (an upper bound on how many at once).
        let repeaterScenarios =
            relays
            |> List.map (fun (r, _) ->
                let rest = relays |> List.map fst |> List.filter (fun x -> x.Id <> r.Id)
                let h = RelayMesh.hops st.Base.Location rest

                let still =
                    rest |> List.choose (fun x -> h.TryFind x.Id |> Option.map (fun k -> (x, k)))

                let cut = sorties |> List.filter (fun s -> not (outOfMesh st still [ s ]).IsEmpty)
                let name = sprintf "repeater %s fails" r.Id

                (name,
                 [
                     event
                         name
                         cut.Length
                         (missionStart st)
                         (sorties
                          |> List.map (fun s -> s.DistanceKm * 1000.0 / droneById.[s.DroneId].CruiseSpeedMs)
                          |> List.fold max 0.0)
                         Ev.Automatic
                         (if cut.IsEmpty then
                              "the mesh routes around it"
                          else
                              "aircraft out of reach fly their lost-link fallback")
                 ]))

        // Before launch nothing is urgent, but someone still has to decide.
        let preLaunch =
            [
                ("a mission drone fails before launch",
                 [
                     event
                         "a mission drone fails before launch"
                         1
                         0.0
                         Ev.decisionTimeS
                         Ev.PilotDecision
                         "approve the re-planned sorties"
                 ])
                match st.Relay with
                | Some(c, _) ->
                    (sprintf "relay carrier %s fails before the mesh is complete" c.Id,
                     [
                         event
                             "relay carrier fails"
                             1
                             0.0
                             Ev.decisionTimeS
                             Ev.PilotDecision
                             "hand the repeaters to the spare carrier"
                     ])
                | None -> ()
            ]

        let workload =
            Ev.Checks.workload st.Pilots (dropoutScenarios @ repeaterScenarios @ preLaunch)


        {
            Ev.Example = "FleetPathPlanning"
            Ev.Operation =
                sprintf
                    "%d waypoints in %d sortie(s) from %s%s, launched %.0f s apart%s"
                    ordered.Length
                    sorties.Length
                    (label st.Base)
                    (match st.Relay with
                     | Some(c, m) -> sprintf " after %s sets down %d repeater(s)" c.Id m.Relays.Length
                     | None -> "")
                    st.LaunchIntervalS
                    (if st.NeedsReturn then
                         ""
                     else
                         ", one way (aircraft expended)")
            Ev.Pilots = st.Pilots
            Ev.Aircraft = fleet.Length
            Ev.PeakAirborne = peak
            Ev.Checks =
                [ coverage; separation; endurance; c2; carrierLift; altitude; replan ]
                @ relayLoss
                @ carrierLoss
                @ expended
                @ [ dropoutCheck; Ev.Checks.pilotRatio st.Pilots fleet.Length peak; workload ]
            Ev.Assumptions =
                [
                    sprintf
                        "Every aircraft waits on its own pad on a %.0f m ring around %s, climbs vertically to the base's altitude, and flies straight legs at its drone's CruiseSpeedMs, with no hover at waypoints and no wind%s."
                        (padRingRadiusM st.Pads.Count)
                        (label st.Base)
                        (if st.NeedsReturn then
                             "; it descends vertically back onto its pad"
                         else
                             "; mission aircraft end in their termination zone")
                    sprintf
                        "The planner's distances (sorties, endurance, re-planning) are base-centred at the base's altitude. The pad offset (%.0f m each way) and the vertical climb and descent at the pad (%.0f m each) add at most %.2f km per flight, inside the %.0f%% battery reserve (the smallest in this fleet is %.2f km)."
                        (padRingRadiusM st.Pads.Count)
                        st.Base.Location.AltitudeMeters
                        (2.0 * (padRingRadiusM st.Pads.Count + st.Base.Location.AltitudeMeters) / 1000.0)
                        Battery.reserveBatteryPercent
                        (fleet
                         |> Array.map (fun d -> d.MaxRangeKm * Battery.reserveBatteryPercent / 100.0)
                         |> Array.fold min Double.MaxValue)
                    "The relay carrier flies first and lands before the mission launches; each repeater is live from the moment it is set down."
                    "A repeater relays with the same radio budget as the aircraft; one link = the same planning range in both directions."
                    "Waypoint and site altitudes are metres above flat ground; height is interpolated linearly between them. Pads are on the ground."
                    "Usable range scales linearly with battery: MaxRangeKm x (1 - reserve), taking MaxRangeKm as still-air range at cruise."
                    sprintf
                        "Separation and C2 are sampled every %.0f s; at up to %.0f m/s closing speed an approach shorter than one step can fall between samples."
                        stepS
                        (2.0 * (fleet |> Array.map (fun d -> d.CruiseSpeedMs) |> Array.fold max 0.0))
                    sprintf
                        "The pilot station is at the base. C2 over %.0f MHz, the ISM band this class of multirotor and small fixed-wing flies; the %.0f dB fade margin covers airframe shadowing, multipath and ground clutter that free-space range ignores."
                        RelayMesh.c2BandMhz
                        RelayMesh.c2FadeMarginDb
                    "The airspace holds no traffic other than this fleet."
                ]
        }

// =============================================================================
// METRICS AND REPORTING
// =============================================================================

type Metrics =
    {
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
        sortie_count: int
        drones_used: int
        unassigned_waypoints: int
        altitude_violations: int
        needs_return: bool
        relays_deployed: int
        relay_carrier: string
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

            printfn
                "║  %s %2d. %-20s (%.4f, %.4f)         ║"
                arrow
                (i + 1)
                wp.Name
                wp.Location.Latitude
                wp.Location.Longitude)

        printfn "╚════════════════════════════════════════════════════════════╝"

    /// Print the repeater mesh the relay carrier sets down, if any.
    let printMesh (relay: (Drone * RelayMesh.Mesh) option) =
        match relay with
        | None -> ()
        | Some(c, mesh) ->
            printfn ""

            printfn "── C2 RELAY MESH (one link %.2f km, <= %d hops) ──" RelayMesh.linkKm Swarm.maxMeshHops

            printfn
                "  %s (%s) sets down %d repeater(s) first, %.2f km deployment flight:"
                c.Id
                c.Model
                mesh.Relays.Length
                mesh.DeployKm

            for r, h in mesh.Relays do
                printfn "    %s %-14s %d hop(s) from the base" r.Id r.Name h

            if not mesh.Uncovered.IsEmpty then
                printfn
                    "  ⚠ %d sampled mission point(s) stay out of reach of every candidate site"
                    mesh.Uncovered.Length

            if not mesh.Fragile.IsEmpty then
                printfn
                    "  ⚠ full redundancy not reachable with these sites: %d sampled point(s) still hang on one repeater"
                    mesh.Fragile.Length

    /// Print the range-feasible fleet decomposition and hard-constraint checks.
    let printFleetPlan (sorties: FleetPlanner.Sortie list) (unassigned: Waypoint list) (altViolations: Waypoint list) =
        printfn ""
        printfn "── FLEET ASSIGNMENT (range-constrained sorties) ──"

        sorties
        |> List.iteri (fun i s ->
            let util =
                if s.RangeKm > 0.0 then
                    s.DistanceKm / s.RangeKm * 100.0
                else
                    0.0

            printfn
                "  Sortie %d → %s (%s): %d waypoints, %.2f/%.1f km (%.0f%% of range)"
                (i + 1)
                s.DroneId
                s.Model
                s.Waypoints.Length
                s.DistanceKm
                s.RangeKm
                util)

        if not (List.isEmpty unassigned) then
            printfn
                "  ⚠ %d waypoint(s) UNASSIGNED: fleet range exhausted (add drones or reduce mission)."
                unassigned.Length

        printfn ""
        printfn "── HARD-CONSTRAINT CHECKS ──"

        if List.isEmpty altViolations then
            printfn "  ✔ Altitude: all waypoints within %.1f m AGL ceiling" Regulations.maxAltitudeAglMeters
        else
            printfn
                "  ✖ Altitude: %d waypoint(s) exceed the %.1f m AGL ceiling:"
                altViolations.Length
                Regulations.maxAltitudeAglMeters

            altViolations
            |> List.iter (fun wp -> printfn "      - %s at %.1f m" wp.Name wp.Location.AltitudeMeters)

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
            printfn "║    --base <id>         Launch/landing waypoint (1st row)   ║"
            printfn "║    --launch-interval <s>  Seconds between launches (10)    ║"
            printfn "║    --pilots <n>        Remote pilots for 1:N evidence (1)  ║"
            printfn "║    --needs-return <b>  true (default) | false: one way,    ║"
            printfn "║                        aircraft expended at last waypoint  ║"
            printfn "║    --relays <path>     C2 repeater candidate sites CSV     ║"
            printfn "║    --relay-mass-kg <m> Mass of one repeater (0.4)          ║"
            printfn "║    --minimal-relays    Fewest repeaters, no redundancy     ║"
            printfn "║    --termination-zones <p> Accepted zones (one-way end)   ║"
            printfn "║    --rtl-base-m <m>    Fallback layer of 1st sortie (60)   ║"
            printfn "║    --rtl-step-m <m>    Layer step between sorties (10)     ║"
            printfn "║    --help              Show this help                      ║"
            printfn "╚════════════════════════════════════════════════════════════╝"
            0
        else
            let sw = Stopwatch.StartNew()

            let waypointsPath = Cli.getOr "waypoints" "examples/Drones/_data/waypoints.csv" args
            let dronesPath = Cli.getOr "drones" "examples/Drones/_data/drones.csv" args

            let outDir =
                Cli.getOr "out" (Path.Combine("runs", "drone", "fleet-path-planning")) args

            let method = Cli.getOr "method" "hybrid" args

            // 1:N evidence options: the base every sortie launches from and lands
            // at (default: the first waypoint in the file), the launch stagger, and
            // the number of remote pilots the permission is asked for.
            let baseId = Cli.tryGet "base" args
            let pilots = max 1 (Cli.getIntOr "pilots" 1 args)

            let launchIntervalS =
                match Cli.tryGet "launch-interval" args with
                | None -> Some 10.0
                | Some s ->
                    match Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture) with
                    | true, v when v >= 0.0 -> Some v
                    | _ -> None

            // Mission criticality: must the aircraft come back, or may they be
            // flown one way and written off in a termination zone?
            let needsReturn =
                match (Cli.getOr "needs-return" "true" args).ToLowerInvariant() with
                | "false"
                | "no"
                | "0" -> false
                | _ -> true

            let relaysPath = Cli.getOr "relays" "examples/Drones/_data/relay_sites.csv" args
            // Resilient by default: no single repeater may cut an aircraft off.
            let redundantRelays = not (Cli.hasFlag "minimal-relays" args)

            let zonesPath =
                Cli.getOr "termination-zones" "examples/Drones/_data/termination_zones.csv" args

            let floatOpt (name: string) (fallback: float) =
                match Cli.tryGet name args with
                | Some s ->
                    match Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture) with
                    | true, v when v > 0.0 -> v
                    | _ -> fallback
                | None -> fallback

            let relayMassKg = floatOpt "relay-mass-kg" 0.4

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

            let zones =
                if File.Exists zonesPath then
                    let zs, errors = Parse.readTerminationZones zonesPath
                    errors |> List.iter (printfn "⚠ Termination zone: %s")
                    zs
                else
                    []

            let relaySites =
                if File.Exists relaysPath then
                    let sites, errors = Parse.readRelaySites relaysPath
                    errors |> List.iter (printfn "⚠ Relay site: %s")
                    sites
                else
                    []

            if not waypointErrors.IsEmpty then
                printfn "⚠ Waypoint parsing errors:"
                waypointErrors |> List.iter (printfn "  - %s")

            if not droneErrors.IsEmpty then
                printfn "⚠ Drone parsing errors:"
                droneErrors |> List.iter (printfn "  - %s")

            let baseWp =
                match baseId with
                | None -> List.tryHead waypoints
                | Some id -> waypoints |> List.tryFind (fun w -> w.Id = id)

            if waypoints.IsEmpty then
                printfn "❌ No waypoints loaded. Exiting."
                1
            elif baseWp.IsNone then
                printfn "❌ Base waypoint '%s' not found in %s. Exiting." (defaultArg baseId "") waypointsPath
                1
            elif launchIntervalS.IsNone then
                printfn "❌ --launch-interval must be a number of seconds >= 0. Exiting."
                1
            else
                let waypointsArr = waypoints |> Array.ofList
                // Default drone specs - cruise speed within FAA Part 107 limit (Regulations.maxGroundSpeedMs = 44.7 m/s)
                let primaryDrone =
                    drones
                    |> List.tryHead
                    |> Option.defaultValue
                        {
                            Id = "DEFAULT"
                            Model = "Generic Multirotor"
                            MaxRangeKm = 20.0
                            MaxPayloadKg = 2.0
                            BatteryCapacityWh = 500.0
                            CruiseSpeedMs = 10.0 // ~36 km/h, well within Regulations.maxGroundSpeedMs
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
                            let nameToIndex = waypointsArr |> Array.mapi (fun i wp -> wp.Name, i) |> Map.ofArray

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
                    let route =
                        PathOptimizer.toOptimizedRoute primaryDrone waypointsArr tour totalDistance

                    printRoute route

                    // The tour visits the base too: start it there, and the rest is
                    // the mission in a good order for sorties out of the base.
                    let baseW = Option.get baseWp
                    let tourWps = tour |> Array.map (fun i -> waypointsArr.[i])

                    let orderedWps =
                        match tourWps |> Array.tryFindIndex (fun w -> w.Id = baseW.Id) with
                        | Some k -> Array.append tourWps.[k + 1 ..] tourWps.[.. k - 1]
                        | None -> tourWps

                    let dronesArr = drones |> Array.ofList

                    // Anything flown beyond one link from the base needs repeaters,
                    // and a drone to set them down; that drone leaves the mission
                    // fleet. Decided on the sampled paths of a trial plan with the
                    // whole fleet, not on the waypoints alone: a one-way sortie's
                    // leg to its termination zone, or a leg between two covered
                    // waypoints, can leave the link circle too.
                    let needsMesh =
                        let trial, _ = FleetPlanner.planSorties baseW needsReturn zones dronesArr orderedWps

                        trial
                        |> List.collect (fun s -> FleetPlanner.sortiePath baseW s |> List.map (fun w -> w.Location))
                        |> RelayMesh.sample
                        |> List.exists (RelayMesh.reachable baseW.Location [] >> not)

                    let carrier =
                        if needsMesh && not relaySites.IsEmpty then
                            RelayMesh.pickCarrier drones
                        else
                            None

                    let missionFleet =
                        dronesArr
                        |> Array.filter (fun d -> carrier |> Option.forall (fun c -> c.Id <> d.Id))

                    let sorties, unassigned =
                        FleetPlanner.planSorties baseW needsReturn zones missionFleet orderedWps

                    // Repeaters to cover every sampled point of every flown path.
                    let relay =
                        carrier
                        |> Option.map (fun c ->
                            let points =
                                sorties
                                |> List.collect (fun s ->
                                    FleetPlanner.sortiePath baseW s |> List.map (fun w -> w.Location))
                                |> RelayMesh.sample

                            (c, RelayMesh.choose baseW.Location redundantRelays relaySites points))

                    let altViolations = FleetPlanner.altitudeViolations orderedWps
                    printFleetPlan sorties unassigned altViolations
                    printMesh relay

                    if needsMesh && relay.IsNone then
                        printfn ""

                        printfn
                            "  ⚠ The flown paths leave one C2 link of the base, but no repeaters can be planned (no candidate sites in %s, or no drone to carry them)."
                            relaysPath

                    let evidence =
                        Evidence.build
                            {
                                Base = baseW
                                Pads = Evidence.pads dronesArr
                                LaunchIntervalS = Option.get launchIntervalS
                                Pilots = pilots
                                NeedsReturn = needsReturn
                                Relay = relay
                                RelayMassKg = relayMassKg
                                RtlBaseM = floatOpt "rtl-base-m" 60.0
                                RtlStepM = max Safety.minSwarmSeparationMeters (floatOpt "rtl-step-m" 10.0)
                                Zones = zones
                            }
                            dronesArr
                            missionFleet
                            orderedWps
                            sorties
                            unassigned

                    FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence.print evidence

                    let dronesUsed =
                        sorties |> List.map (fun s -> s.DroneId) |> List.distinct |> List.length

                    sw.Stop()

                    // Write results
                    let waypointsSha = Data.fileSha256Hex waypointsPath
                    let dronesSha = Data.fileSha256Hex dronesPath

                    let metrics: Metrics =
                        {
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
                            sortie_count = List.length sorties
                            drones_used = dronesUsed
                            unassigned_waypoints = List.length unassigned
                            altitude_violations = List.length altViolations
                            needs_return = needsReturn
                            relays_deployed =
                                relay |> Option.map (fun (_, m) -> m.Relays.Length) |> Option.defaultValue 0
                            relay_carrier = relay |> Option.map (fun (c, _) -> c.Id) |> Option.defaultValue ""
                            elapsed_ms = sw.ElapsedMilliseconds
                        }

                    Reporting.writeJson (Path.Combine(outDir, "metrics.json")) metrics
                    FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence.write outDir evidence

                    // Write route as CSV
                    let routeRows =
                        route.Waypoints
                        |> List.mapi (fun i wp ->
                            [
                                string (i + 1)
                                wp.Id
                                wp.Name
                                sprintf "%.6f" wp.Location.Latitude
                                sprintf "%.6f" wp.Location.Longitude
                                sprintf "%.1f" wp.Location.AltitudeMeters
                            ])

                    Reporting.writeCsv
                        (Path.Combine(outDir, "optimized_route.csv"))
                        [ "sequence"; "waypoint_id"; "name"; "latitude"; "longitude"; "altitude_m" ]
                        routeRows

                    let table =
                        route.Waypoints
                        |> List.mapi (fun i wp ->
                            sprintf
                                "| %d | %s | %s | %.4f | %.4f | %.0f |"
                                (i + 1)
                                wp.Id
                                wp.Name
                                wp.Location.Latitude
                                wp.Location.Longitude
                                wp.Location.AltitudeMeters)
                        |> String.concat "\n"

                    // Write report
                    let meshLine =
                        match relay with
                        | Some(c, m) ->
                            sprintf
                                "%s sets down %d repeater(s): %s"
                                c.Id
                                m.Relays.Length
                                (m.Relays
                                 |> List.map (fun (r, h) -> sprintf "%s (%d hop)" r.Id h)
                                 |> String.concat ", ")
                        | None when needsMesh ->
                            "none deployed, although the flown paths leave one link of the base (no candidate sites, or no drone to carry them)"
                        | None -> "none needed (the base reaches every flown path)"

                    let report =
                        $"""# Drone Fleet Path Planning Results

## Summary

- **Run ID**: {runId}
- **Method**: {methodUsed}
- **Waypoints**: {waypoints.Length}
- **Total Distance**: {totalDistance:F2} km
- **Estimated Flight Time**: {route.EstimatedFlightTimeMin:F1} min
- **Energy Consumption**: {route.EnergyConsumptionWh:F1} Wh
- **Elapsed Time**: {sw.ElapsedMilliseconds} ms
- **Aircraft return**: {(if needsReturn then
                             "yes"
                         else
                             "no: one way, expended at the last waypoint")}
- **C2 relay mesh**: {meshLine}
- **1:N Permission Evidence**: {FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence.verdict evidence} (see `permission-evidence.md`)

## Optimized Route

| # | Waypoint | Name | Latitude | Longitude | Altitude (m) |
|---|----------|------|----------|-----------|--------------|
{table}

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
- `permission-evidence.md` / `permission-evidence.json` - One-pilot-to-many (1:N) permission evidence pack
"""

                    Reporting.writeTextFile (Path.Combine(outDir, "run-report.md")) report

                    printfn ""
                    printfn "Results written to: %s" outDir
                    0
                else
                    printfn "❌ Optimization failed"
                    1
