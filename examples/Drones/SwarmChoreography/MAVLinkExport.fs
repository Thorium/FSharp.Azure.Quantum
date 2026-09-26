/// MAVLink Export Module
///
/// Converts quantum-optimized drone formation assignments to MAVLink-compatible
/// formats for ArduPilot, PX4, and other autopilot systems.
///
/// Supports:
/// - QGroundControl mission plan (.plan JSON)
/// - MAVLink waypoint file (.waypoints)
/// - ArduPilot parameter files (.parm)
/// - An F# show script (mavlink_show.fsx) that uploads, verifies and flies it
///
/// MAVLink is the de-facto standard for drone communication, supporting
/// hundreds of autopilot boards from manufacturers like Holybro, CUAV,
/// mRo, and others.
///
/// Reference: https://mavlink.io/en/
module FSharp.Azure.Quantum.Examples.Drones.SwarmChoreography.MAVLinkExport

open System
open System.IO
open System.Text

open FSharp.Azure.Quantum.Examples.Drones.MavlinkMission

// =============================================================================
// SHOW TYPES
// =============================================================================

/// Show metadata for MAVLink export
type MavShowMetadata =
    {
        Title: string
        GeneratedBy: string
        GeneratedAt: DateTimeOffset
        OptimizationMethod: string
        NumDrones: int
        TotalWaypoints: int
        HomePosition: GeoCoordinate option
        /// The show's local origin (formation coordinates are offsets from it).
        ShowOrigin: GeoCoordinate
        /// Top horizontal speed; each leg's own speed is set by DO_CHANGE_SPEED.
        CruiseSpeedMs: float
        /// Which formations (indices into the show) the waypoints are, in order.
        FormationIndices: int list
    }

/// Multi-drone swarm mission
type SwarmMission =
    {
        Metadata: MavShowMetadata
        Missions: DroneMission list
    }

// =============================================================================
// SHOW COORDINATES
// =============================================================================

/// Convert our Position3D (X=right, Y=forward, Z=up) to Local NED
let position3DToLocalNed (x: float) (y: float) (z: float) (scale: float) : LocalPosition =
    {
        North = y * scale // Forward -> North
        East = x * scale // Right -> East
        Down = -z * scale
    } // Up -> -Down

/// Convert our Position3D to geo coordinate relative to home
let position3DToGeo (home: GeoCoordinate) (x: float) (y: float) (z: float) (scale: float) : GeoCoordinate =
    let local = position3DToLocalNed x y z scale
    localToGeo home local

/// A mission point: lat/lon from the offset, and Altitude RELATIVE to home.
/// Mission items use MAV_FRAME_GLOBAL_RELATIVE_ALT (and QGC AltitudeMode 1), so
/// the home's own MSL altitude must not be added: with --home-alt 150 that put
/// every waypoint 150 m above the ground.
let position3DToMissionPoint (home: GeoCoordinate) (x: float) (y: float) (z: float) (scale: float) : GeoCoordinate =
    { position3DToGeo home x y z scale with
        Altitude = z * scale
    }


// =============================================================================
// MISSION BUILDING
// =============================================================================

/// Formations re-indexed BY DRONE: Positions.[i] is where drone i flies in that
/// formation. The optimiser decides which drone takes which slot
/// (Assignments: DroneId -> TargetPositionIndex), so a formation's own slot
/// order is not a flight plan. Formation 0 is the start, where drone i stands on
/// slot i; formation k + 1 is the target of transitions.[k]. The mapping mirrors
/// the choreography loop: the first assignment of a drone wins, and a drone
/// without one keeps slot i.
let assignedFormations
    (transitions:
        {|
            FromFormation: string
            ToFormation: string
            Assignments:
                {|
                    DroneId: int
                    TargetPositionIndex: int
                |}[]
            TotalDistance: float
            Method: string
        |}[])
    (formations:
        {|
            Name: string
            Positions: {| X: float; Y: float; Z: float |}[]
        |}[])
    =
    formations
    |> Array.mapi (fun k f ->
        match Array.tryItem (k - 1) transitions with
        | Some t ->
            {| f with
                Positions =
                    f.Positions
                    |> Array.mapi (fun drone own ->
                        t.Assignments
                        |> Array.tryFind (fun a -> a.DroneId = drone)
                        |> Option.map (fun a -> f.Positions.[a.TargetPositionIndex])
                        |> Option.defaultValue own)
            |}
        | None -> f)

// =============================================================================
// TIMING: every drone flies every transition in the same time
// =============================================================================

/// A point in metres from the show origin (X east, Y north, Z up above home).
type LocalPoint = { X: float; Y: float; Z: float }

/// ArduPilot Copter speed limits the timing model relies on (its defaults).
/// A waypoint leg's horizontal speed is the mission speed (DO_CHANGE_SPEED,
/// ground speed); its vertical rate is capped by WPNAV_SPEED_UP / WPNAV_SPEED_DN.
module Autopilot =
    /// WPNAV_SPEED_UP 250 cm/s
    let climbSpeedMs = 2.5
    /// WPNAV_SPEED_DN 150 cm/s
    let descentSpeedMs = 1.5
    /// Slowest leg speed exported; a shorter leg is flown at this speed and the
    /// drone waits out the rest of the transition at the waypoint.
    let minLegSpeedMs = 0.5
    /// Hold at every formation, identical for all drones.
    let formationHoldS = 2.0
    /// NAV_TAKEOFF altitude, the same for every drone so all start the first
    /// transition together.
    let takeoffAltitudeM = 2.0
    /// LAND_ALT_LOW 1000 cm: below it a landing slows to LAND_SPEED.
    let landAltLowM = 10.0
    /// LAND_SPEED 50 cm/s
    let landSpeedMs = 0.5
    /// RTL_LOIT_TIME 5000 ms: hover above home before landing.
    let rtlLoiterS = 5.0
    /// WPNAV_ACCEL 250 cm/s/s: horizontal acceleration limit of waypoint legs.
    let accelMss = 2.5
    /// WPNAV_ACCEL_Z 100 cm/s/s: vertical acceleration limit.
    let accelZMss = 1.0
    /// WPNAV_JERK 1 m/s/s/s: horizontal jerk limit (ArduPilot's S-curves).
    let jerkMsss = 1.0
    /// PSC_JERK_Z 5 m/s/s/s: vertical jerk limit.
    let jerkZMsss = 5.0

/// Kinematic limits of one straight leg, along the track: the horizontal part
/// is held to the leg's speed and WPNAV_ACCEL / WPNAV_JERK, the vertical part to
/// the climb or descent speed, WPNAV_ACCEL_Z and PSC_JERK_Z; along a sloped
/// track each limit is divided by its axis's share of the track.
let private trackLimits (speedMs: float) (a: LocalPoint) (b: LocalPoint) =
    let length = Math.Sqrt((b.X - a.X) ** 2.0 + (b.Y - a.Y) ** 2.0 + (b.Z - a.Z) ** 2.0)
    let horizontal = Math.Sqrt((b.X - a.X) ** 2.0 + (b.Y - a.Y) ** 2.0)
    let dz = b.Z - a.Z
    let ch = if length > 0.0 then horizontal / length else 0.0
    let cz = if length > 0.0 then abs dz / length else 0.0

    let verticalSpeed =
        if dz >= 0.0 then
            Autopilot.climbSpeedMs
        else
            Autopilot.descentSpeedMs

    let limit (h: float) (z: float) =
        min
            (if ch > 1e-9 then h / ch else Double.PositiveInfinity)
            (if cz > 1e-9 then z / cz else Double.PositiveInfinity)

    (length,
     limit speedMs verticalSpeed,
     limit Autopilot.accelMss Autopilot.accelZMss,
     limit Autopilot.jerkMsss Autopilot.jerkZMsss)

/// Time and distance to go from rest to `v` (or back) with acceleration up to
/// `amax` and jerk up to `jmax`: a jerk-limited S-curve, symmetric, so the
/// distance is v * time / 2.
let private rampTo (v: float) (amax: float) (jmax: float) =
    let t =
        if v <= amax * amax / jmax then
            2.0 * Math.Sqrt(v / jmax)
        else
            v / amax + amax / jmax

    (t, v * t / 2.0)

/// The leg's profile: the peak speed actually reached and the ramp time. A
/// short leg never reaches its speed limit; its peak is where the two ramps meet.
let private legProfile (speedMs: float) (a: LocalPoint) (b: LocalPoint) =
    let length, vmax, amax, jmax = trackLimits speedMs a b

    if length < 1e-9 then
        (length, 0.0, 0.0, 0.0, amax, jmax)
    else
        let ramp, rampDistance = rampTo vmax amax jmax

        if 2.0 * rampDistance <= length then
            (length, vmax, ramp, length / vmax + ramp, amax, jmax)
        else
            // Bisect the peak speed whose two ramps cover the leg exactly.
            let rec peak lo hi i =
                let mid = (lo + hi) / 2.0

                if i = 0 then
                    mid
                elif 2.0 * snd (rampTo mid amax jmax) > length then
                    peak lo mid (i - 1)
                else
                    peak mid hi (i - 1)

            let v = peak 0.0 vmax 60
            let ramp, _ = rampTo v amax jmax
            (length, v, ramp, 2.0 * ramp, amax, jmax)

/// Seconds to fly a straight leg at horizontal speed `speedMs`, starting and
/// ending at rest (every leg ends at a waypoint hold) with ArduPilot's
/// acceleration and jerk limits: an S-curve, not constant speed.
let legSeconds (speedMs: float) (a: LocalPoint) (b: LocalPoint) =
    let _, _, _, total, _, _ = legProfile speedMs a b
    total

/// Distance covered `t` seconds into a ramp from rest to `v`.
let private rampDistance (v: float) (amax: float) (jmax: float) (ramp: float) (t: float) =
    // Integrate the jerk profile numerically: +jmax, (hold amax), -jmax.
    let tj = if v <= amax * amax / jmax then ramp / 2.0 else amax / jmax
    let steps = max 1 (int (Math.Ceiling(t / 0.005)))
    let dt = t / float steps

    let jerkAt (s: float) =
        if s < tj then jmax
        elif s < ramp - tj then 0.0
        else -jmax

    let _, _, distance =
        Seq.init steps id
        |> Seq.fold
            (fun (acc, vel, dist) i ->
                let s = float i * dt
                let acc' = acc + jerkAt (s + dt / 2.0) * dt
                let vel' = vel + (acc + acc') / 2.0 * dt
                (acc', vel', dist + (vel + vel') / 2.0 * dt))
            (0.0, 0.0, 0.0)

    distance

/// The leg as the vehicle flies it, sampled every `stepS` from its start:
/// (seconds since the leg started, position). Straight line, S-curve timing.
let legSamples (stepS: float) (speedMs: float) (a: LocalPoint) (b: LocalPoint) : (float * LocalPoint)[] =
    let length, v, ramp, total, amax, jmax = legProfile speedMs a b

    if length < 1e-9 || total <= 0.0 then
        [| (0.0, b) |]
    else
        let rampLength = rampDistance v amax jmax ramp ramp

        let along (t: float) =
            if t <= ramp then
                rampDistance v amax jmax ramp t
            elif t >= total - ramp then
                length - rampDistance v amax jmax ramp (total - t)
            else
                rampLength + v * (t - ramp)

        let at (t: float) =
            let f = (along t / length) |> max 0.0 |> min 1.0

            {
                X = a.X + f * (b.X - a.X)
                Y = a.Y + f * (b.Y - a.Y)
                Z = a.Z + f * (b.Z - a.Z)
            }

        let n = max 1 (int (Math.Ceiling(total / stepS)))
        Array.init (n + 1) (fun i -> let t = min total (float i * total / float n) in (t, (if i = n then b else at t)))

/// One transition flown by all drones together: T is the slowest drone's leg
/// time at cruise speed; every drone gets the horizontal speed that makes its
/// own leg (S-curve and all) take T, so all arrive together. A leg that cannot
/// be flown that slowly (minimum speed, or bound by its vertical limits) is
/// flown as slowly as it can and the difference is added to the drone's hold,
/// so every drone also LEAVES the waypoint at the same moment.
/// Returns (speed m/s, hold s) per drone.
let syncTransition (cruiseMs: float) (holdS: float) (legs: (LocalPoint * LocalPoint)[]) =
    let t =
        legs |> Array.map (fun (a, b) -> legSeconds cruiseMs a b) |> Array.fold max 0.0

    legs
    |> Array.map (fun (a, b) ->
        let slowest = legSeconds Autopilot.minLegSpeedMs a b

        let speed =
            if legSeconds cruiseMs a b >= t - 1e-9 then
                cruiseMs
            elif slowest <= t then
                Autopilot.minLegSpeedMs
            else
                // legSeconds falls as the speed rises: bisect for time T.
                let rec find lo hi i =
                    let mid = (lo + hi) / 2.0

                    if i = 0 then hi
                    elif legSeconds mid a b > t then find mid hi (i - 1)
                    else find lo mid (i - 1)

                find Autopilot.minLegSpeedMs cruiseMs 50

        (speed, holdS + max 0.0 (t - legSeconds speed a b)))

// =============================================================================
// FAILSAFE: per-drone RTL altitudes and parameter files
// =============================================================================

/// RTL altitude per drone: all above the highest show point, `spacingM` apart,
/// so drones returning at once cross at different heights. ArduPilot's RTL
/// first climbs VERTICALLY to RTL_ALT, so the order matters too: a drone that
/// is ever stacked under another (closer than `spacingM` horizontally) gets
/// the lower RTL altitude, or its climb would go through the other drone.
/// Conflicting stackings are broken by average show height.
let rtlAltitudes (spacingM: float) (routes: LocalPoint[][]) : float[] =
    let n = routes.Length

    let top =
        routes |> Array.collect id |> Array.map (fun p -> p.Z) |> Array.fold max 0.0

    let lowest = Math.Ceiling(top + spacingM)

    // Drone a must return above drone b.
    let above a b =
        Array.zip routes.[a] routes.[b]
        |> Array.exists (fun (pa, pb) ->
            Math.Sqrt((pa.X - pb.X) ** 2.0 + (pa.Y - pb.Y) ** 2.0) < spacingM && pa.Z > pb.Z)

    let meanZ d =
        routes.[d] |> Array.averageBy (fun p -> p.Z)

    // Lowest RTL first: a drone that no remaining drone must be below.
    let rec order remaining acc =
        match remaining with
        | [] -> List.rev acc
        | _ ->
            let free =
                remaining
                |> List.filter (fun d -> remaining |> List.forall (fun o -> o = d || not (above d o)))

            let next = (if free.IsEmpty then remaining else free) |> List.minBy meanZ
            order (List.filter ((<>) next) remaining) (next :: acc)

    let ranks =
        order [ 0 .. n - 1 ] [] |> List.mapi (fun rank d -> (d, rank)) |> Map.ofList

    Array.init n (fun d -> lowest + float ranks.[d] * spacingM)

/// RTL_LOIT_TIME for all drones: at least the longest RTL climb-and-transit
/// any drone can have from anywhere in the show. A drone that got home first
/// then waits above it until every other drone has also finished its transit,
/// so no one descends through a lower RTL layer while another drone is still
/// crossing it. Capped at ArduPilot's 60 s maximum.
let rtlLoiterSeconds (cruiseMs: float) (routes: LocalPoint[][]) (homes: LocalPoint[]) (rtlAlts: float[]) =
    let worst =
        [
            for d in 0 .. routes.Length - 1 do
                for p in routes.[d] do
                    let climb = max 0.0 (rtlAlts.[d] - p.Z) / Autopilot.climbSpeedMs

                    let transit =
                        Math.Sqrt((p.X - homes.[d].X) ** 2.0 + (p.Y - homes.[d].Y) ** 2.0) / cruiseMs

                    climb + transit
        ]
        |> List.fold max Autopilot.rtlLoiterS

    min 60.0 (Math.Ceiling worst)

/// ArduPilot parameters one vehicle must carry for the exported mission to fly
/// as planned and for its failsafe to be the RTL the evidence pack models.
let parameters (cruiseMs: float) (rtlAltM: float) (rtlLoiterS: float) : (string * float) list =
    [
        "RTL_ALT", Math.Round(rtlAltM * 100.0) // cm
        "RTL_ALT_FINAL", 0.0 // land at home
        "RTL_CONE_SLOPE", 0.0 // always climb to RTL_ALT
        "RTL_CLIMB_MIN", 0.0
        "RTL_LOIT_TIME", rtlLoiterS * 1000.0 // ms
        "RTL_SPEED", Math.Round(cruiseMs * 100.0) // cm/s
        "WPNAV_SPEED_UP", Autopilot.climbSpeedMs * 100.0
        "WPNAV_SPEED_DN", Autopilot.descentSpeedMs * 100.0
        "WPNAV_ACCEL", Autopilot.accelMss * 100.0 // the S-curve timing assumes these four
        "WPNAV_ACCEL_Z", Autopilot.accelZMss * 100.0
        "WPNAV_JERK", Autopilot.jerkMsss
        "PSC_JERK_Z", Autopilot.jerkZMsss
        "LAND_SPEED", Autopilot.landSpeedMs * 100.0
        "LAND_SPEED_HIGH", 0.0 // = WPNAV_SPEED_DN
        "LAND_ALT_LOW", Autopilot.landAltLowM * 100.0
        "FS_GCS_ENABLE", 1.0 // ground-station link lost: RTL outside AUTO ...
        "FS_THR_ENABLE", 1.0 // RC link lost: RTL outside AUTO ...
        // ... but in AUTO (the show) and while landing, CONTINUE: bits 0, 1, 3.
        // The mission itself is deconflicted; a lone vertical RTL climb from
        // under a stacked drone is not.
        "FS_OPTIONS", 11.0
        // Low battery: warn only. The pilot takes the drone out of the show by
        // the drop-out procedure (step out of the formation plane, land there);
        // the endurance margin keeps the critical level out of the show.
        "BATT_FS_LOW_ACT", 0.0
    ]

/// Build mission items for one drone: take off where it stands, then for each
/// waypoint set the leg speed and fly there with its hold, then land back on
/// its own start slot. NAV_LAND with a position flies there at the current
/// height and descends; an RTL would instead return to wherever home is set.
let buildMissionItems
    (toGeo: LocalPoint -> GeoCoordinate)
    (cruiseMs: float)
    (start: LocalPoint)
    (legs: (LocalPoint * float * float) list)
    : MissionItem list =

    let mutable seq = 0

    let nextSeq () =
        let s = seq
        seq <- seq + 1
        s

    let items = ResizeArray<MissionItem>()

    // Takeoff (ArduPilot climbs where the drone was armed: its start slot)
    items.Add(takeoff Autopilot.takeoffAltitudeM (toGeo start) (nextSeq ()))

    for p, speed, hold in legs do
        items.Add(setSpeed speed (nextSeq ()))
        // 0.5 m acceptance radius. The hold goes in as whole seconds (all
        // ArduPilot keeps); its fraction follows as a NAV_DELAY.
        let whole = Math.Floor hold
        items.Add(waypoint whole 0.5 (toGeo p) (nextSeq ()))

        if hold - whole > 1e-3 then
            items.Add(navDelay (Math.Round(hold - whole, 3)) (nextSeq ()))

    // Land on its own start slot
    items.Add(setSpeed cruiseMs (nextSeq ()))
    items.Add(landAt (toGeo start) (nextSeq ()))

    items |> Seq.toList

/// The flown plan of a show at `scale`: which formations are waypoints, each
/// drone's route through them, where it takes off, and the synchronised speed
/// and hold of every drone in every transition.
let private showPlan
    (scale: float)
    (transitionSpeed: float)
    (formations:
        {|
            Name: string
            Positions: {| X: float; Y: float; Z: float |}[]
        |}[])
    (numDrones: int)
    =
    let pointOf (p: {| X: float; Y: float; Z: float |}) =
        {
            X = p.X * scale
            Y = p.Y * scale
            Z = p.Z * scale
        }

    // The formations flown as waypoints (their indices in the show): the
    // airborne ones. A ground formation is where the drones stand: they take off
    // from it and land on it, never fly to it mid-show at 0 m.
    let picked =
        [ 0 .. formations.Length - 1 ]
        |> List.filter (fun k -> formations.[k].Positions |> Array.exists (fun p -> p.Z > 0.0))

    // Per drone: its formation points in show order.
    let routes =
        [|
            for d in 0 .. numDrones - 1 ->
                picked
                |> List.map (fun k -> pointOf formations.[k].Positions.[d])
                |> Array.ofList
        |]

    // Each drone arms and takes off on its slot of the first formation.
    let starts = formations.[0].Positions |> Array.map pointOf

    let takeoffPoints =
        starts
        |> Array.map (fun s ->
            { s with
                Z = Autopilot.takeoffAltitudeM
            })

    // Transition k: every drone from its previous point (the take-off point for
    // k = 0) to its k-th point, synchronised.
    let schedule =
        [
            for k in 0 .. routes.[0].Length - 1 ->
                routes
                |> Array.mapi (fun d r -> ((if k = 0 then takeoffPoints.[d] else r.[k - 1]), r.[k]))
                |> syncTransition transitionSpeed Autopilot.formationHoldS
        ]

    (picked, routes, starts, takeoffPoints, schedule)

/// Closest two drones come while flying the plan's take-off and transitions,
/// with the S-curve timing (sampled every 0.05 s; a planning figure, which the
/// evidence pack then checks exactly on the exported missions).
let showSeparation
    (scale: float)
    (transitionSpeed: float)
    (formations:
        {|
            Name: string
            Positions: {| X: float; Y: float; Z: float |}[]
        |}[])
    (numDrones: int)
    =
    let _, routes, starts, takeoffPoints, schedule =
        showPlan scale transitionSpeed formations numDrones

    let track d =
        let climb = legSamples 0.05 Autopilot.climbSpeedMs starts.[d] takeoffPoints.[d]

        let _, _, samples =
            schedule
            |> List.indexed
            |> List.fold
                (fun (t: float, here: LocalPoint, acc: (float * LocalPoint) list) (k, perDrone) ->
                    let speed, hold = perDrone.[d]

                    let leg =
                        legSamples 0.05 speed here routes.[d].[k]
                        |> Array.map (fun (dt, p) -> (t + dt, p))

                    let tEnd = fst (Array.last leg)
                    (tEnd + hold, routes.[d].[k], (tEnd + hold, routes.[d].[k]) :: (List.rev (List.ofArray leg) @ acc)))
                (fst (Array.last climb), takeoffPoints.[d], List.rev (List.ofArray climb))

        samples |> List.rev |> Array.ofList

    let tracks = Array.init numDrones track

    let at (samples: (float * LocalPoint)[]) (t: float) =
        match samples |> Array.tryFindIndex (fun (ti, _) -> ti >= t) with
        | None -> snd (Array.last samples)
        | Some 0 -> snd samples.[0]
        | Some i ->
            let t0, p0 = samples.[i - 1]
            let t1, p1 = samples.[i]
            let f = if t1 > t0 then (t - t0) / (t1 - t0) else 1.0

            {
                X = p0.X + f * (p1.X - p0.X)
                Y = p0.Y + f * (p1.Y - p0.Y)
                Z = p0.Z + f * (p1.Z - p0.Z)
            }

    let tEnd = tracks |> Array.map (fun s -> fst (Array.last s)) |> Array.max

    [ 0.0 .. 0.05 .. tEnd ]
    |> List.map (fun t ->
        let here = tracks |> Array.map (fun s -> at s t)

        [
            for i in 0 .. numDrones - 1 do
                for j in i + 1 .. numDrones - 1 do
                    let a, b = here.[i], here.[j]

                    if a.Z >= 0.1 || b.Z >= 0.1 then
                        Math.Sqrt((a.X - b.X) ** 2.0 + (a.Y - b.Y) ** 2.0 + (a.Z - b.Z) ** 2.0)
        ]
        |> List.fold min Double.PositiveInfinity)
    |> List.fold min Double.PositiveInfinity

/// Create a complete swarm mission from show data
let createSwarmMission
    (title: string)
    (homePosition: GeoCoordinate)
    (scale: float)
    (transitionSpeed: float)
    (formations:
        {|
            Name: string
            Positions: {| X: float; Y: float; Z: float |}[]
        |}[])
    (numDrones: int)
    (rtlSpacingM: float)
    (optimizationMethod: string)
    : SwarmMission =

    let drones =
        [
            for i in 0 .. numDrones - 1 ->
                {
                    SystemId = i + 1
                    ComponentId = 1
                    Name = sprintf "Drone%d" i
                    // ArduPilot SITL instance i serves MAVLink on TCP 5760 + 10 i.
                    ConnectionString = sprintf "tcp:127.0.0.1:%d" (5760 + i * 10)
                }
        ]

    let toGeo (p: LocalPoint) =
        position3DToMissionPoint homePosition p.X p.Y p.Z 1.0

    let picked, routes, starts, takeoffPoints, schedule =
        showPlan scale transitionSpeed formations numDrones

    let rtlAlts = rtlAltitudes rtlSpacingM routes

    let rtlLoiter =
        rtlLoiterSeconds
            transitionSpeed
            (Array.map2 (fun r t -> Array.append [| t |] r) routes takeoffPoints)
            starts
            rtlAlts

    let missions =
        drones
        |> List.mapi (fun i drone ->
            let legs =
                schedule
                |> List.mapi (fun k perDrone ->
                    let speed, hold = perDrone.[i]
                    (routes.[i].[k], speed, hold))

            let items = buildMissionItems toGeo transitionSpeed starts.[i] legs

            {
                Drone = drone
                // Its arming point, at the site's MSL altitude (plannedHomePosition
                // and .waypoints row 0 are absolute).
                HomePosition =
                    { toGeo starts.[i] with
                        Altitude = homePosition.Altitude
                    }
                Items = items
                Parameters = parameters transitionSpeed rtlAlts.[i] rtlLoiter
            })

    let totalWaypoints = missions |> List.sumBy (fun m -> m.Items.Length)

    {
        Metadata =
            {
                Title = title
                GeneratedBy = "FSharp.Azure.Quantum SwarmChoreography"
                GeneratedAt = DateTimeOffset.UtcNow
                OptimizationMethod = optimizationMethod
                NumDrones = numDrones
                TotalWaypoints = totalWaypoints
                HomePosition = Some homePosition
                ShowOrigin = homePosition
                CruiseSpeedMs = transitionSpeed
                FormationIndices = picked
            }
        Missions = missions
    }


// =============================================================================
// FILE EXPORT (the writers live in MavlinkMission)
// =============================================================================

/// The show's missions as the launcher flies them: all start at T0 together,
/// because the synchronised timing depends on it.
let scheduled (swarm: SwarmMission) : ScheduledMission list =
    swarm.Missions |> List.map (fun m -> { Mission = m; LaunchS = 0.0 })

// =============================================================================
// HIGH-LEVEL EXPORT API
// =============================================================================

/// Export a complete show to all MAVLink formats. `formations` must be indexed by
/// drone (see assignedFormations). Returns the mission that was written, so the
/// caller can check exactly what the drones will fly.
let exportShow
    (baseDir: string)
    (title: string)
    (homePosition: GeoCoordinate)
    (scale: float)
    (transitionSpeed: float)
    (formations:
        {|
            Name: string
            Positions: {| X: float; Y: float; Z: float |}[]
        |}[])
    (numDrones: int)
    (rtlSpacingM: float)
    (optimizationMethod: string)
    : SwarmMission =

    // Create swarm mission
    let swarm =
        createSwarmMission title homePosition scale transitionSpeed formations numDrones rtlSpacingM optimizationMethod

    // Ensure output directory exists
    Directory.CreateDirectory(baseDir) |> ignore

    // Export all formats
    QGroundControl.writeAll baseDir swarm.Missions
    WaypointFile.writeAll baseDir swarm.Missions
    ParamFile.writeAll baseDir swarm.Missions
    FsxScript.writeFile (Path.Combine(baseDir, "mavlink_show.fsx")) (scheduled swarm)

    printfn ""
    printfn "MAVLink export complete!"
    printfn "  - QGroundControl plans: %d files" swarm.Missions.Length
    printfn "  - Waypoint files: %d files" swarm.Missions.Length
    printfn "  - Parameter files: %d files (load before flight)" swarm.Missions.Length
    printfn "  - F# show script: mavlink_show.fsx (dotnet fsi mavlink_show.fsx --dry-run)"
    swarm

/// Convenience function to create show from transition results (matches CrazyflieExport API)
let fromTransitionResults
    (transitions:
        {|
            FromFormation: string
            ToFormation: string
            Assignments:
                {|
                    DroneId: int
                    TargetPositionIndex: int
                |}[]
            TotalDistance: float
            Method: string
        |}[])
    (formations:
        {|
            Name: string
            Positions: {| X: float; Y: float; Z: float |}[]
        |}[])
    (homePosition: GeoCoordinate)
    (scale: float)
    (transitionSpeed: float)
    : SwarmMission =

    let numDrones =
        formations
        |> Array.tryHead
        |> Option.map (fun f -> f.Positions.Length)
        |> Option.defaultValue 4

    let quantumCount =
        transitions
        |> Array.filter (fun t -> t.Method.Contains("Quantum"))
        |> Array.length

    let optimizationMethod =
        if quantumCount > transitions.Length / 2 then
            "Quantum (QAOA)"
        elif quantumCount > 0 then
            "Hybrid (Quantum + Classical)"
        else
            "Classical (Greedy)"

    createSwarmMission
        "Quantum Drone Choreography"
        homePosition
        scale
        transitionSpeed
        (assignedFormations transitions formations)
        numDrones
        FSharp.Azure.Quantum.Examples.Drones.Domain.Safety.minSwarmSeparationMeters
        optimizationMethod
