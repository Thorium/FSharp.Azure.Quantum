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

// =============================================================================
// DOMAIN TYPES
// =============================================================================

/// WGS84 coordinate for real-world positioning
type GeoCoordinate =
    {
        Latitude: float // Decimal degrees
        Longitude: float // Decimal degrees
        Altitude: float // Meters above sea level (AMSL) or relative
    }

/// Local NED (North-East-Down) position relative to home
type LocalPosition =
    {
        North: float // Meters, positive = north
        East: float // Meters, positive = east
        Down: float // Meters, positive = down (negative = up)
    }

/// MAVLink command types (subset relevant for choreography)
type MavCmd =
    | NavWaypoint // MAV_CMD_NAV_WAYPOINT (16)
    | NavLoiterTime // MAV_CMD_NAV_LOITER_TIME (19)
    | NavReturnToLaunch // MAV_CMD_NAV_RETURN_TO_LAUNCH (20)
    | NavLand // MAV_CMD_NAV_LAND (21)
    | NavTakeoff // MAV_CMD_NAV_TAKEOFF (22)
    | NavLoiterToAlt // MAV_CMD_NAV_LOITER_TO_ALT (31)
    | NavDelay // MAV_CMD_NAV_DELAY (93)
    | DoSetMode // MAV_CMD_DO_SET_MODE (176)
    | DoSetServo // MAV_CMD_DO_SET_SERVO (183)
    | DoSetRelay // MAV_CMD_DO_SET_RELAY (181)
    | DoChangeSpeed // MAV_CMD_DO_CHANGE_SPEED (178)
    | DoSetRoiLocation // MAV_CMD_DO_SET_ROI_LOCATION (195)
    | DoSetLedColor // Custom for LED control

/// MAVLink frame types
type MavFrame =
    | Global // MAV_FRAME_GLOBAL (0) - WGS84 absolute
    | GlobalRelativeAlt // MAV_FRAME_GLOBAL_RELATIVE_ALT (3) - WGS84 lat/lon, relative alt
    | LocalNed // MAV_FRAME_LOCAL_NED (1) - North-East-Down relative to home
    | LocalEnu // MAV_FRAME_LOCAL_ENU (4) - East-North-Up relative to home
    | Mission // MAV_FRAME_MISSION (2) - Mission item specific

/// A single mission item in MAVLink format
type MissionItem =
    {
        Sequence: int
        Command: MavCmd
        Frame: MavFrame
        Param1: float // Command-specific
        Param2: float // Command-specific
        Param3: float // Command-specific
        Param4: float // Yaw angle (degrees)
        Latitude: float
        Longitude: float
        Altitude: float
        Autocontinue: bool
    }

/// Drone configuration for MAVLink systems
type MavDroneConfig =
    {
        SystemId: int // MAVLink system ID (1-255)
        ComponentId: int // MAVLink component ID (usually 1 for autopilot)
        Name: string
        ConnectionString: string // "tcp:HOST:PORT" or "udpin:PORT" (see mavlink_show.fsx)
    }

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

/// Complete MAVLink mission for a single drone
type DroneMission =
    {
        Drone: MavDroneConfig
        /// Where this drone arms (its own start slot): ArduPilot's home, so RTL
        /// brings it back here, not to one point shared by the swarm.
        HomePosition: GeoCoordinate
        Items: MissionItem list
        /// ArduPilot parameters the vehicle must carry for the mission to fly as
        /// exported (written to <DroneName>.parm).
        Parameters: (string * float) list
    }

/// Multi-drone swarm mission
type SwarmMission =
    {
        Metadata: MavShowMetadata
        Missions: DroneMission list
    }

// =============================================================================
// COORDINATE CONVERSION
// =============================================================================

/// Earth radius in meters (WGS84 mean)
[<Literal>]
let private EarthRadiusMeters = 6371000.0

/// Convert local position offset to geo coordinate
let localToGeo (home: GeoCoordinate) (local: LocalPosition) : GeoCoordinate =
    let latRad = home.Latitude * Math.PI / 180.0
    let metersPerDegreeLat = Math.PI * EarthRadiusMeters / 180.0
    let metersPerDegreeLon = metersPerDegreeLat * Math.Cos(latRad)

    {
        Latitude = home.Latitude + local.North / metersPerDegreeLat
        Longitude = home.Longitude + local.East / metersPerDegreeLon
        Altitude = home.Altitude - local.Down
    } // Down is negative altitude

/// Inverse of localToGeo: a geo coordinate as a local offset from home. Used to
/// read the exported missions back as metres, so anything checked about them is
/// checked on exactly what was written.
let geoToLocal (home: GeoCoordinate) (pos: GeoCoordinate) : LocalPosition =
    let latRad = home.Latitude * Math.PI / 180.0
    let metersPerDegreeLat = Math.PI * EarthRadiusMeters / 180.0
    let metersPerDegreeLon = metersPerDegreeLat * Math.Cos(latRad)

    {
        North = (pos.Latitude - home.Latitude) * metersPerDegreeLat
        East = (pos.Longitude - home.Longitude) * metersPerDegreeLon
        Down = home.Altitude - pos.Altitude
    }

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
// MAV COMMAND ENCODING
// =============================================================================

/// Get MAVLink command ID
let private mavCmdId =
    function
    | NavWaypoint -> 16
    | NavLoiterTime -> 19
    | NavReturnToLaunch -> 20
    | NavLand -> 21
    | NavTakeoff -> 22
    | NavLoiterToAlt -> 31
    | NavDelay -> 93
    | DoSetMode -> 176
    | DoSetServo -> 183
    | DoSetRelay -> 181
    | DoChangeSpeed -> 178
    | DoSetRoiLocation -> 195
    | DoSetLedColor -> 999 // Custom extension

/// Get MAVLink frame ID
let private mavFrameId =
    function
    | Global -> 0
    | GlobalRelativeAlt -> 3
    | LocalNed -> 1
    | LocalEnu -> 4
    | Mission -> 2

// =============================================================================
// MISSION ITEM BUILDERS (Idiomatic F# with partial application)
// =============================================================================

/// Create a mission item with common defaults
let private createItem seq cmd frame lat lon alt =
    {
        Sequence = seq
        Command = cmd
        Frame = frame
        Param1 = 0.0
        Param2 = 0.0
        Param3 = 0.0
        Param4 = 0.0 // Yaw: 0 = don't change
        Latitude = lat
        Longitude = lon
        Altitude = alt
        Autocontinue = true
    }

/// Create a takeoff command
let takeoff (altitude: float) (home: GeoCoordinate) (seq: int) : MissionItem =
    { createItem seq NavTakeoff GlobalRelativeAlt home.Latitude home.Longitude altitude with
        Param1 = 0.0 // Pitch angle (ignored for multirotors)
        Param4 = 0.0
    } // Yaw: 0 = maintain current heading

/// Create a waypoint command
let waypoint (holdTime: float) (acceptRadius: float) (pos: GeoCoordinate) (seq: int) : MissionItem =
    { createItem seq NavWaypoint GlobalRelativeAlt pos.Latitude pos.Longitude pos.Altitude with
        Param1 = holdTime // Hold time in seconds
        Param2 = acceptRadius
    } // Acceptance radius in meters

/// Create a delay: hold position for a number of seconds. Unlike a waypoint's
/// hold (param1, stored by ArduPilot as whole seconds), NAV_DELAY keeps its
/// seconds as a float, so it carries the fraction of a synchronised hold.
/// Params 2-4 (-1) disable the time-of-day form.
let navDelay (seconds: float) (seq: int) : MissionItem =
    { createItem seq NavDelay Mission 0.0 0.0 0.0 with
        Param1 = seconds
        Param2 = -1.0
        Param3 = -1.0
        Param4 = -1.0
    }

/// Create a loiter (hover) command
let loiter (duration: float) (pos: GeoCoordinate) (seq: int) : MissionItem =
    { createItem seq NavLoiterTime GlobalRelativeAlt pos.Latitude pos.Longitude pos.Altitude with
        Param1 = duration
    } // Loiter time in seconds

/// Create a land command
let landAt (pos: GeoCoordinate) (seq: int) : MissionItem =
    createItem seq NavLand GlobalRelativeAlt pos.Latitude pos.Longitude 0.0

/// Create a return-to-launch command
let returnToLaunch (seq: int) : MissionItem =
    createItem seq NavReturnToLaunch GlobalRelativeAlt 0.0 0.0 0.0

/// Create a speed change command
let setSpeed (speedMs: float) (seq: int) : MissionItem =
    { createItem seq DoChangeSpeed Mission 0.0 0.0 0.0 with
        Param1 = 1.0 // Speed type: 1 = ground speed
        Param2 = speedMs // Speed in m/s
        Param3 = -1.0
    } // Throttle: -1 = no change

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
// QGROUNDCONTROL PLAN EXPORT (.plan JSON)
// =============================================================================

module QGroundControl =

    /// Escape string for JSON
    let private escapeJson (s: string) =
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t")

    /// Format float for JSON (avoid locale issues)
    let private formatFloat (f: float) =
        if Double.IsNaN(f) then
            "null"
        elif Double.IsInfinity(f) then
            "null"
        else
            f.ToString("G15", System.Globalization.CultureInfo.InvariantCulture)

    /// Generate QGroundControl mission plan JSON for a single drone
    let toJson (mission: DroneMission) : string =
        let sb = StringBuilder()

        sb.AppendLine("{") |> ignore
        sb.AppendLine("  \"fileType\": \"Plan\",") |> ignore

        sb.AppendLine("  \"geoFence\": { \"circles\": [], \"polygons\": [], \"version\": 2 },")
        |> ignore

        sb.AppendLine("  \"groundStation\": \"FSharp.Azure.Quantum\",") |> ignore

        // Mission section
        sb.AppendLine("  \"mission\": {") |> ignore
        sb.AppendLine("    \"cruiseSpeed\": 5,") |> ignore
        sb.AppendLine("    \"firmwareType\": 3,") |> ignore // 3 = ArduPilot
        sb.AppendLine("    \"globalPlanAltitudeMode\": 1,") |> ignore // 1 = Relative
        sb.AppendLine("    \"hoverSpeed\": 3,") |> ignore

        // Items array
        sb.AppendLine("    \"items\": [") |> ignore

        let itemCount = mission.Items.Length

        for i, item in mission.Items |> List.indexed do
            sb.AppendLine("      {") |> ignore
            sb.AppendLine("        \"AMSLAltAboveTerrain\": null,") |> ignore

            sb.AppendLine(sprintf "        \"Altitude\": %s," (formatFloat item.Altitude))
            |> ignore

            sb.AppendLine("        \"AltitudeMode\": 1,") |> ignore // 1 = Relative

            sb.AppendLine(sprintf "        \"autoContinue\": %s," (if item.Autocontinue then "true" else "false"))
            |> ignore

            sb.AppendLine(sprintf "        \"command\": %d," (mavCmdId item.Command))
            |> ignore

            sb.AppendLine(sprintf "        \"doJumpId\": %d," (i + 1)) |> ignore

            sb.AppendLine(sprintf "        \"frame\": %d," (mavFrameId item.Frame))
            |> ignore

            sb.AppendLine("        \"params\": [") |> ignore
            sb.AppendLine(sprintf "          %s," (formatFloat item.Param1)) |> ignore
            sb.AppendLine(sprintf "          %s," (formatFloat item.Param2)) |> ignore
            sb.AppendLine(sprintf "          %s," (formatFloat item.Param3)) |> ignore
            sb.AppendLine(sprintf "          %s," (formatFloat item.Param4)) |> ignore
            sb.AppendLine(sprintf "          %s," (formatFloat item.Latitude)) |> ignore
            sb.AppendLine(sprintf "          %s," (formatFloat item.Longitude)) |> ignore
            sb.AppendLine(sprintf "          %s" (formatFloat item.Altitude)) |> ignore
            sb.AppendLine("        ],") |> ignore
            sb.AppendLine("        \"type\": \"SimpleItem\"") |> ignore

            if i < itemCount - 1 then
                sb.AppendLine("      },") |> ignore
            else
                sb.AppendLine("      }") |> ignore

        sb.AppendLine("    ],") |> ignore

        // Planned home position
        sb.AppendLine("    \"plannedHomePosition\": [") |> ignore

        sb.AppendLine(sprintf "      %s," (formatFloat mission.HomePosition.Latitude))
        |> ignore

        sb.AppendLine(sprintf "      %s," (formatFloat mission.HomePosition.Longitude))
        |> ignore

        sb.AppendLine(sprintf "      %s" (formatFloat mission.HomePosition.Altitude))
        |> ignore

        sb.AppendLine("    ],") |> ignore
        sb.AppendLine("    \"vehicleType\": 2,") |> ignore // 2 = Quadrotor
        sb.AppendLine("    \"version\": 2") |> ignore
        sb.AppendLine("  },") |> ignore

        // Rally points (empty)
        sb.AppendLine("  \"rallyPoints\": { \"points\": [], \"version\": 2 },")
        |> ignore

        sb.AppendLine("  \"version\": 1") |> ignore
        sb.AppendLine("}") |> ignore

        sb.ToString()

    /// Write mission to QGroundControl .plan file
    let writeFile (path: string) (mission: DroneMission) =
        File.WriteAllText(path, toJson mission)
        printfn "Wrote QGroundControl plan to: %s" path

    /// Write all drone missions to separate .plan files
    let writeSwarmFiles (baseDir: string) (swarm: SwarmMission) =
        Directory.CreateDirectory(baseDir) |> ignore

        for mission in swarm.Missions do
            let filename = sprintf "%s_mission.plan" mission.Drone.Name
            let path = Path.Combine(baseDir, filename)
            writeFile path mission

// =============================================================================
// MAVLINK WAYPOINT FILE EXPORT (.waypoints)
// =============================================================================

module WaypointFile =

    /// Generate MAVLink waypoint file content
    /// Format: QGC WPL 110 (version 110)
    let toWaypointFormat (mission: DroneMission) : string =
        let sb = StringBuilder()

        // Header
        sb.AppendLine("QGC WPL 110") |> ignore

        // Home position (index 0)
        sb.AppendLine(
            sprintf
                "0\t1\t0\t16\t0\t0\t0\t0\t%.8f\t%.8f\t%.2f\t1"
                mission.HomePosition.Latitude
                mission.HomePosition.Longitude
                mission.HomePosition.Altitude
        )
        |> ignore

        // Mission items
        for item in mission.Items do
            // Format: INDEX CURRENT_WP COORD_FRAME COMMAND P1 P2 P3 P4 LAT LON ALT AUTOCONTINUE
            // Row 0 is home; mission items follow from 1 (they are 0-based here).
            let current = 0

            sb.AppendLine(
                sprintf
                    "%d\t%d\t%d\t%d\t%.4f\t%.4f\t%.4f\t%.4f\t%.8f\t%.8f\t%.2f\t%d"
                    (item.Sequence + 1)
                    current
                    (mavFrameId item.Frame)
                    (mavCmdId item.Command)
                    item.Param1
                    item.Param2
                    item.Param3
                    item.Param4
                    item.Latitude
                    item.Longitude
                    item.Altitude
                    (if item.Autocontinue then 1 else 0)
            )
            |> ignore

        sb.ToString()

    /// Write mission to .waypoints file
    let writeFile (path: string) (mission: DroneMission) =
        File.WriteAllText(path, toWaypointFormat mission)
        printfn "Wrote MAVLink waypoints to: %s" path

    /// Write all drone missions to separate .waypoints files
    let writeSwarmFiles (baseDir: string) (swarm: SwarmMission) =
        Directory.CreateDirectory(baseDir) |> ignore

        for mission in swarm.Missions do
            let filename = sprintf "%s.waypoints" mission.Drone.Name
            let path = Path.Combine(baseDir, filename)
            writeFile path mission

// =============================================================================
// ARDUPILOT PARAMETER FILE EXPORT (.parm)
// =============================================================================

/// One "NAME VALUE" line per parameter, with # comments (the MAVProxy /
/// ArduPilot defaults format, loaded with `param load` or a GCS's param loader).
module ParamFile =

    let toText (mission: DroneMission) : string =
        let sb = StringBuilder()

        sb.AppendLine(sprintf "# %s: failsafe and speed parameters for the exported show" mission.Drone.Name)
        |> ignore

        sb.AppendLine("# Load before flight and verify on the vehicle; the mission assumes these values.")
        |> ignore

        for name, value in mission.Parameters do
            sb.AppendLine(sprintf "%s %s" name (value.ToString("R", Globalization.CultureInfo.InvariantCulture)))
            |> ignore

        sb.ToString()

    /// Parse a parameter file back (the evidence pack checks what was written).
    let parse (text: string) : Map<string, float> =
        text.Split('\n')
        |> Array.map (fun l -> l.Trim())
        |> Array.filter (fun l -> l <> "" && not (l.StartsWith "#"))
        |> Array.choose (fun l ->
            match l.Split([| ' '; '\t'; ',' |], StringSplitOptions.RemoveEmptyEntries) with
            | [| name; value |] ->
                match
                    Double.TryParse(
                        value,
                        Globalization.NumberStyles.Float,
                        Globalization.CultureInfo.InvariantCulture
                    )
                with
                | true, v -> Some(name, v)
                | _ -> None
            | _ -> None)
        |> Map.ofArray

    let fileName (mission: DroneMission) = sprintf "%s.parm" mission.Drone.Name

    let writeSwarmFiles (baseDir: string) (swarm: SwarmMission) =
        Directory.CreateDirectory(baseDir) |> ignore

        for mission in swarm.Missions do
            let path = Path.Combine(baseDir, fileName mission)
            File.WriteAllText(path, toText mission)
            printfn "Wrote ArduPilot parameters to: %s" path

// =============================================================================
// F# SHOW SCRIPT EXPORT (mavlink_show.fsx)
// =============================================================================

/// The generated mavlink_show.fsx: flies the exported show over MAVLink 2 with
/// the NuGet package "MAVLink" (ArduPilot Mission Planner's C# MAVLink). Before
/// anything flies it sets and reads back every parameter from <Drone>.parm,
/// uploads <Drone>_mission.plan and reads it back, checks each vehicle stands
/// on its declared home, and refuses to start on any difference: the vehicles
/// then carry exactly what the evidence pack checked. `--dry-run` builds every
/// message from the files and prints it, without touching the network.
module FsxScript =

    let private template =
        """
// mavlink_show.fsx - generated by FSharp.Azure.Quantum SwarmChoreography.
//
// Flies the exported show on ArduPilot Copter vehicles over MAVLink 2.
// TRY IT AGAINST ARDUPILOT SITL BEFORE ANY REAL AIRCRAFT.
//
//   dotnet fsi mavlink_show.fsx --dry-run   loads the files, builds every message,
//                                           prints what it would send; no network
//   dotnet fsi mavlink_show.fsx             connects and flies
//
// Per drone it connects, sets every parameter from <Drone>.parm and reads each one
// back, uploads <Drone>_mission.plan and reads the whole mission back, and checks
// the vehicle stands on its declared home (its own slot). It REFUSES to start
// unless every vehicle carries exactly the mission and parameters the evidence
// pack checked. Then it arms all, starts all missions at the same moment and
// streams telemetry. Ctrl+C sends every drone RTL (the modelled abort).
//
// MAVLink library: the NuGet package "MAVLink" (ArduPilot Mission Planner's
// generated C# MAVLink, https://github.com/ArduPilot/MissionPlanner/tree/master/ExtLibs/Mavlink).

#r "nuget: MAVLink, 1.0.8"

// MISSION_REQUEST (non-INT) is deprecated in the library but still answered, for
// vehicles that request items that way.
#nowarn "44"

open System
open System.Collections.Concurrent
open System.IO
open System.Net
open System.Net.Sockets
open System.Text
open System.Text.Json
open System.Threading

// =============================================================================
// CONFIGURATION
// =============================================================================

/// Drone name (as in the exported files) and connection:
///   "tcp:HOST:PORT"   e.g. SITL instance i serves tcp:127.0.0.1:(5760 + 10 i)
///   "udpin:PORT"      listen for a vehicle or MAVProxy --out=udp:127.0.0.1:PORT
let drones =
    [
{{DRONES}}
    ]

/// How far (m) a vehicle may stand from its declared home before arming.
let homeToleranceM = 2.0

let dryRun = fsi.CommandLineArgs |> Array.contains "--dry-run"
let dir = __SOURCE_DIRECTORY__

// =============================================================================
// THE EXPORTED FILES (what the evidence pack checked)
// =============================================================================

type Item =
    {
        Command: int
        Frame: int
        P: float[] // params 1-7: p1-p4, latitude, longitude, altitude
    }

let loadPlan (name: string) =
    use doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, name + "_mission.plan")))
    let mission = doc.RootElement.GetProperty("mission")
    let num (e: JsonElement) = if e.ValueKind = JsonValueKind.Number then e.GetDouble() else 0.0
    let home = mission.GetProperty("plannedHomePosition").EnumerateArray() |> Seq.map num |> Array.ofSeq

    let items =
        mission.GetProperty("items").EnumerateArray()
        |> Seq.map (fun i ->
            {
                Command = i.GetProperty("command").GetInt32()
                Frame = i.GetProperty("frame").GetInt32()
                P = i.GetProperty("params").EnumerateArray() |> Seq.map num |> Array.ofSeq
            })
        |> List.ofSeq

    (home, items)

let loadParams (name: string) =
    File.ReadAllLines(Path.Combine(dir, name + ".parm"))
    |> Array.map (fun l -> l.Trim())
    |> Array.filter (fun l -> l <> "" && not (l.StartsWith "#"))
    |> Array.map (fun l ->
        match l.Split([| ' '; '\t'; ',' |], StringSplitOptions.RemoveEmptyEntries) with
        | [| n; v |] -> (n, Double.Parse(v, Globalization.CultureInfo.InvariantCulture))
        | _ -> failwithf "%s.parm: bad line '%s'" name l)
    |> List.ofArray

// The mission as uploaded: seq 0 is home (ArduPilot replaces it with where the
// vehicle arms), the exported items follow from seq 1.
let missionOf (home: float[]) (items: Item list) =
    let homeItem =
        {
            Command = 16
            Frame = 0
            P = [| 0.0; 0.0; 0.0; 0.0; home.[0]; home.[1]; home.[2] |]
        }

    homeItem :: items

// =============================================================================
// MAVLINK
// =============================================================================

let gcsSystem, gcsComponent = 255uy, 190uy

let paramId (name: string) =
    let bytes = Array.zeroCreate<byte> 16
    let src = Encoding.ASCII.GetBytes name
    Array.blit src 0 bytes 0 (min 16 src.Length)
    bytes

let paramName (bytes: byte[]) =
    Encoding.ASCII.GetString(bytes).TrimEnd('\000')

// MAVLink _INT frames for MISSION_ITEM_INT: GLOBAL -> GLOBAL_INT, RELATIVE_ALT ->
// RELATIVE_ALT_INT; MISSION (commands without a position) stays.
let intFrame =
    function
    | 0 -> 5uy
    | 3 -> 6uy
    | f -> byte f

let isPositional cmd = cmd = 16 || cmd = 21 || cmd = 22

let missionItemInt (target: byte) (seq: int) (i: Item) =
    let positional = isPositional i.Command

    let x = if positional then int (Math.Round(i.P.[4] * 1e7)) else 0
    let y = if positional then int (Math.Round(i.P.[5] * 1e7)) else 0
    let z = if positional then float32 i.P.[6] else 0.0f

    // Arguments in the struct's field order: param1-4, x (lat 1e7), y (lon
    // 1e7), z, seq, command, target system/component, frame, current,
    // autocontinue, mission_type (0 = mission).
    MAVLink.mavlink_mission_item_int_t(
        float32 i.P.[0],
        float32 i.P.[1],
        float32 i.P.[2],
        float32 i.P.[3],
        x,
        y,
        z,
        uint16 seq,
        uint16 i.Command,
        target,
        1uy,
        intFrame i.Frame,
        0uy,
        1uy,
        0uy
    )

let commandLong (target: byte) (command: int) (p: float list) =
    let p = Array.ofList (p @ List.replicate (7 - p.Length) 0.0)

    MAVLink.mavlink_command_long_t(
        float32 p.[0],
        float32 p.[1],
        float32 p.[2],
        float32 p.[3],
        float32 p.[4],
        float32 p.[5],
        float32 p.[6],
        uint16 command,
        target,
        1uy,
        0uy
    )

// ArduPilot Copter custom modes and the commands used
let modeGuided, modeRtl = 4.0, 6.0
let cmdSetMode, cmdArm, cmdMissionStart, cmdSetMessageInterval = 176, 400, 300, 511

/// A byte stream over UDP datagrams (MavlinkParse reads from a Stream). The
/// first datagram's sender becomes the peer that replies go to.
type DatagramStream(client: UdpClient) =
    inherit Stream()
    let mutable pending: byte[] = [||]
    let mutable offset = 0
    member val Peer: IPEndPoint = null with get, set
    override _.CanRead = true
    override _.CanSeek = false
    override _.CanWrite = false
    override _.Length = raise (NotSupportedException())
    override _.Position with get () = raise (NotSupportedException()) and set _ = raise (NotSupportedException())
    override _.Flush() = ()
    override _.Seek(_, _) = raise (NotSupportedException())
    override _.SetLength _ = raise (NotSupportedException())
    override _.Write(_, _, _) = raise (NotSupportedException())

    override this.Read(buffer: byte[], start: int, count: int) =
        if offset >= pending.Length then
            let mutable from = IPEndPoint(IPAddress.Any, 0)
            pending <- client.Receive(&from)
            offset <- 0

            if isNull this.Peer then
                this.Peer <- from

        let n = min count (pending.Length - offset)
        Array.blit pending offset buffer start n
        offset <- offset + n
        n

/// One vehicle connection: a reader thread fills the inbox; requests wait for a
/// matching reply with a timeout and retry.
type Link(name: string, connection: string) =
    let inbox = new BlockingCollection<MAVLink.MAVLinkMessage>()
    let writer = MAVLink.MavlinkParse(false)
    let sendLock = obj ()
    let mutable seq = 0

    let reader, send =
        match connection.Split(':') with
        | [| "tcp"; host; port |] ->
            let client = new TcpClient(host, int port)
            let stream = client.GetStream()
            (stream :> Stream, (fun (bytes: byte[]) -> stream.Write(bytes, 0, bytes.Length)))
        | [| "udpin"; port |] ->
            let client = new UdpClient(int port)
            let stream = new DatagramStream(client)

            (stream :> Stream,
             (fun (bytes: byte[]) ->
                 if not (isNull stream.Peer) then
                     client.Send(bytes, bytes.Length, stream.Peer) |> ignore))
        | _ -> failwithf "%s: unsupported connection '%s' (use tcp:HOST:PORT or udpin:PORT)" name connection

    let thread =
        Thread(
            (fun () ->
                let parser = MAVLink.MavlinkParse(false)

                while true do
                    try
                        match parser.ReadPacket(reader) with
                        | null -> ()
                        | m -> inbox.Add m
                    with
                    | :? TimeoutException -> ()
                    | :? IOException as e -> eprintfn "%s: link error: %s" name e.Message; Thread.Sleep 500),
            IsBackground = true
        )

    do thread.Start()

    let take (timeoutMs: int) =
        let mutable m: MAVLink.MAVLinkMessage = null
        if inbox.TryTake(&m, timeoutMs) then Some m else None

    member val Target = 1uy with get, set
    member _.Name = name

    /// Wait for the vehicle's heartbeat and talk to its system id from then on.
    member this.WaitHeartbeat(timeoutMs: int) =
        let until = DateTime.UtcNow.AddMilliseconds(float timeoutMs)

        let rec next () =
            let left = int (until - DateTime.UtcNow).TotalMilliseconds

            if left <= 0 then
                false
            else
                match take left with
                | Some m when m.msgid = uint32 MAVLink.MAVLINK_MSG_ID.HEARTBEAT
                               && (unbox<MAVLink.mavlink_heartbeat_t> m.data).``type`` <> 6uy ->
                    this.Target <- m.sysid
                    true
                | Some _ -> next ()
                | None -> false

        next ()

    member _.Send(msgid: MAVLink.MAVLINK_MSG_ID, payload: obj) =
        lock sendLock (fun () ->
            let bytes = writer.GenerateMAVLinkPacket20(msgid, payload, false, gcsSystem, gcsComponent, seq)
            seq <- (seq + 1) % 256
            send bytes)

    /// The next message with this id that satisfies `accept`, or None.
    member _.Receive(msgid: MAVLink.MAVLINK_MSG_ID, accept: obj -> bool, timeoutMs: int) =
        let until = DateTime.UtcNow.AddMilliseconds(float timeoutMs)

        let rec next () =
            let left = int (until - DateTime.UtcNow).TotalMilliseconds

            if left <= 0 then
                None
            else
                match take left with
                | Some m when m.msgid = uint32 msgid && accept m.data -> Some m.data
                | Some _ -> next ()
                | None -> None

        next ()

    /// Send, wait for the reply, resend up to `tries` times.
    member this.Request(msgid, payload: obj, reply, accept, ?tries) =
        let rec go n =
            this.Send(msgid, payload)

            match this.Receive(reply, accept, 1500) with
            | Some r -> r
            | None when n > 1 -> go (n - 1)
            | None -> failwithf "%s: no %A reply to %A" name reply msgid

        go (defaultArg tries 5)

type MsgId = MAVLink.MAVLINK_MSG_ID

let heartbeat () =
    // custom_mode, type (MAV_TYPE_GCS), autopilot (MAV_AUTOPILOT_INVALID),
    // base_mode, system_status (MAV_STATE_ACTIVE), mavlink_version
    MAVLink.mavlink_heartbeat_t(0u, 6uy, 8uy, 0uy, 4uy, 3uy)

let setParam (link: Link) (name: string) (value: float) =
    let msg =
        MAVLink.mavlink_param_set_t(
            float32 value,
            link.Target,
            1uy,
            paramId name,
            9uy // MAV_PARAM_TYPE_REAL32
        )

    link.Request(MsgId.PARAM_SET, msg, MsgId.PARAM_VALUE, (fun d -> paramName (unbox<MAVLink.mavlink_param_value_t> d).param_id = name))
    |> ignore

let readParam (link: Link) (name: string) =
    let msg =
        MAVLink.mavlink_param_request_read_t(-1s, link.Target, 1uy, paramId name)

    let reply =
        link.Request(MsgId.PARAM_REQUEST_READ, msg, MsgId.PARAM_VALUE, (fun d -> paramName (unbox<MAVLink.mavlink_param_value_t> d).param_id = name))

    float (unbox<MAVLink.mavlink_param_value_t> reply).param_value

let uploadMission (link: Link) (mission: Item list) =
    let items = Array.ofList mission
    let count = MAVLink.mavlink_mission_count_t(uint16 items.Length, link.Target, 1uy, 0uy)
    link.Send(MsgId.MISSION_COUNT, count)

    let rec serve () =
        let request =
            link.Receive(MsgId.MISSION_REQUEST_INT, (fun _ -> true), 3000)
            |> Option.map (fun d -> int (unbox<MAVLink.mavlink_mission_request_int_t> d).seq)
            |> Option.orElse (
                link.Receive(MsgId.MISSION_REQUEST, (fun _ -> true), 1)
                |> Option.map (fun d -> int (unbox<MAVLink.mavlink_mission_request_t> d).seq)
            )

        match request with
        | Some s ->
            link.Send(MsgId.MISSION_ITEM_INT, missionItemInt link.Target s items.[s])

            if s < items.Length - 1 then
                serve ()
        | None -> failwithf "%s: mission upload stalled" link.Name

    serve ()

    match link.Receive(MsgId.MISSION_ACK, (fun _ -> true), 5000) with
    | Some d when (unbox<MAVLink.mavlink_mission_ack_t> d).``type`` = 0uy -> ()
    | Some d -> failwithf "%s: mission rejected (MAV_MISSION_RESULT %d)" link.Name (unbox<MAVLink.mavlink_mission_ack_t> d).``type``
    | None -> failwithf "%s: no MISSION_ACK" link.Name

let downloadMission (link: Link) =
    let list = MAVLink.mavlink_mission_request_list_t(link.Target, 1uy, 0uy)
    let count = link.Request(MsgId.MISSION_REQUEST_LIST, list, MsgId.MISSION_COUNT, (fun _ -> true))
    let n = int (unbox<MAVLink.mavlink_mission_count_t> count).count

    let items =
        [
            for s in 0 .. n - 1 ->
                let request =
                    MAVLink.mavlink_mission_request_int_t(uint16 s, link.Target, 1uy, 0uy)

                link.Request(MsgId.MISSION_REQUEST_INT, request, MsgId.MISSION_ITEM_INT, (fun d -> int (unbox<MAVLink.mavlink_mission_item_int_t> d).seq = s))
                |> unbox<MAVLink.mavlink_mission_item_int_t>
        ]

    link.Send(MsgId.MISSION_ACK, MAVLink.mavlink_mission_ack_t(link.Target, 1uy, 0uy, 0uy))
    items

/// Differences between what the files say and what the vehicle holds; home
/// (seq 0) is left out: ArduPilot sets it where the vehicle arms.
let missionDifferences (expected: Item list) (actual: MAVLink.mavlink_mission_item_int_t list) =
    [
        if expected.Length <> actual.Length then
            sprintf "mission has %d items, the files %d" actual.Length expected.Length
        else
            for s, (e, a) in List.indexed (List.zip expected actual) |> List.skip 1 do
                let near (x: float) (y: float) tolerance = abs (x - y) <= tolerance

                if int a.command <> e.Command then
                    sprintf "seq %d: command %d, the files %d" s a.command e.Command
                else
                    let ok =
                        match e.Command with
                        | 16 -> near (float a.param1) (Math.Floor e.P.[0]) 1e-6
                        | 178 -> near (float a.param1) e.P.[0] 1e-6 && near (float a.param2) e.P.[1] 1e-4
                        | 93 -> near (float a.param1) e.P.[0] 1e-3
                        | _ -> true

                    let position =
                        match e.Command with
                        | 16
                        | 21 ->
                            abs (a.x - int (Math.Round(e.P.[4] * 1e7))) <= 1
                            && abs (a.y - int (Math.Round(e.P.[5] * 1e7))) <= 1
                            && near (float a.z) e.P.[6] 0.01
                        | 22 -> near (float a.z) e.P.[6] 0.01
                        | _ -> true

                    if not (ok && position) then
                        sprintf
                            "seq %d (command %d): p1 %g p2 %g at (%d, %d, %g), the files p1 %g p2 %g at (%.0f, %.0f, %g)"
                            s
                            e.Command
                            a.param1
                            a.param2
                            a.x
                            a.y
                            a.z
                            e.P.[0]
                            e.P.[1]
                            (e.P.[4] * 1e7)
                            (e.P.[5] * 1e7)
                            e.P.[6]
    ]

let command (link: Link) (cmd: int) (p: float list) =
    let reply =
        link.Request(MsgId.COMMAND_LONG, commandLong link.Target cmd p, MsgId.COMMAND_ACK, (fun d -> int (unbox<MAVLink.mavlink_command_ack_t> d).command = cmd))

    match (unbox<MAVLink.mavlink_command_ack_t> reply).result with
    | 0uy -> ()
    | r -> failwithf "%s: command %d refused (MAV_RESULT %d)" link.Name cmd r

let distanceM (lat1: float) (lon1: float) (lat2: float) (lon2: float) =
    let r = 6371000.0
    let dLat = (lat2 - lat1) * Math.PI / 180.0
    let dLon = (lon2 - lon1) * Math.PI / 180.0 * Math.Cos(lat1 * Math.PI / 180.0)
    r * Math.Sqrt(dLat * dLat + dLon * dLon)

// =============================================================================
// DRY RUN: every message, no network
// =============================================================================

let files =
    drones
    |> List.map (fun (name, connection) ->
        let home, items = loadPlan name
        (name, connection, home, missionOf home items, loadParams name))

if dryRun then
    let writer = MAVLink.MavlinkParse(false)
    let packet msgid (payload: obj) = writer.GenerateMAVLinkPacket20(msgid, payload, false, gcsSystem, gcsComponent, 0)
    let hex (b: byte[]) = b |> Array.map (sprintf "%02x") |> String.concat " "

    for name, connection, home, mission, parms in files do
        printfn "== %s via %s: home %.7f, %.7f (%.1f m MSL)" name connection home.[0] home.[1] home.[2]

        let paramPackets =
            parms
            |> List.map (fun (n, v) ->
                packet MsgId.PARAM_SET (MAVLink.mavlink_param_set_t(float32 v, 1uy, 1uy, paramId n, 9uy)))

        printfn "  %d PARAM_SET (each read back with PARAM_REQUEST_READ): %s" parms.Length (parms |> List.map (fun (n, v) -> sprintf "%s=%g" n v) |> String.concat " ")
        printfn "  first PARAM_SET packet: %s" (hex paramPackets.Head)
        printfn "  MISSION_COUNT %d, then MISSION_ITEM_INT on request:" mission.Length

        for s, i in List.indexed mission do
            let bytes = packet MsgId.MISSION_ITEM_INT (missionItemInt 1uy s i)
            printfn "    seq %2d cmd %3d frame %d p1 %8.3f p2 %6.3f  %11.7f %11.7f %7.2f  (%d bytes)" s i.Command (intFrame i.Frame) i.P.[0] i.P.[1] i.P.[4] i.P.[5] i.P.[6] bytes.Length

        printfn "  preflight: vehicle within %.1f m of home, mission and parameters read back equal, then GUIDED, arm, MISSION_START" homeToleranceM
        printfn "  SITL: sim_vehicle.py -v ArduCopter -I <instance> --custom-location=%.7f,%.7f,%.1f,0" home.[0] home.[1] home.[2]

    printfn "Dry run: nothing sent."
    exit 0

// =============================================================================
// PREFLIGHT: refuse unless every vehicle matches the files
// =============================================================================

let links = files |> List.map (fun (name, connection, _, _, _) -> Link(name, connection))

// Our heartbeat, once a second, to every vehicle
let heartbeatTimer =
    new Timer((fun _ -> for l in links do l.Send(MsgId.HEARTBEAT, heartbeat ())), null, 0, 1000)

let problems =
    List.zip links files
    |> List.collect (fun (link, (name, _, home, mission, parms)) ->
        printfn "%s: waiting for heartbeat..." name

        if not (link.WaitHeartbeat 30000) then
            [ sprintf "%s: no heartbeat" name ]
        else
            printfn "%s: setting %d parameters..." name parms.Length

            for n, v in parms do
                setParam link n v

            let paramProblems =
                [
                    for n, v in parms do
                        let actual = readParam link n

                        if abs (actual - v) > 1e-3 * max 1.0 (abs v) then
                            sprintf "%s: %s = %g, the files %g" name n actual v
                ]

            printfn "%s: uploading %d mission items..." name mission.Length
            uploadMission link mission
            let missionProblems = downloadMission link |> missionDifferences mission |> List.map (fun p -> name + ": " + p)

            // Stream position and mission progress
            command link cmdSetMessageInterval [ 33.0; 500000.0 ] // GLOBAL_POSITION_INT at 2 Hz
            command link cmdSetMessageInterval [ 42.0; 1000000.0 ] // MISSION_CURRENT at 1 Hz

            let homeProblems =
                match link.Receive(MsgId.GLOBAL_POSITION_INT, (fun _ -> true), 10000) with
                | None -> [ sprintf "%s: no position" name ]
                | Some d ->
                    let p = unbox<MAVLink.mavlink_global_position_int_t> d
                    let off = distanceM (float p.lat / 1e7) (float p.lon / 1e7) home.[0] home.[1]

                    if off > homeToleranceM then
                        [ sprintf "%s: stands %.1f m from its declared home (its own slot)" name off ]
                    else
                        []

            paramProblems @ missionProblems @ homeProblems)

if not problems.IsEmpty then
    eprintfn "REFUSING TO START: the vehicles do not match what the evidence pack checked:"

    for p in problems do
        eprintfn "  %s" p

    exit 1

printfn "Every vehicle carries exactly the checked mission and parameters."

// =============================================================================
// FLY
// =============================================================================

let aborted = ref false

Console.CancelKeyPress.Add(fun e ->
    e.Cancel <- true

    if not aborted.Value then
        aborted.Value <- true
        eprintfn "ABORT: RTL for every drone"

        for l in links do
            try
                command l cmdSetMode [ 1.0; modeRtl ]
            with ex ->
                eprintfn "%s: %s" l.Name ex.Message)

for l in links do
    command l cmdSetMode [ 1.0; modeGuided ]
    command l cmdArm [ 1.0 ]

// Every mission starts at the same moment: the synchronised timing depends on it.
for l in links do
    l.Send(MsgId.COMMAND_LONG, commandLong l.Target cmdMissionStart [ 0.0; 0.0 ])

printfn "Missions started. Ctrl+C = RTL for all."

while true do
    for l in links do
        let position = l.Receive(MsgId.GLOBAL_POSITION_INT, (fun _ -> true), 200)
        let current = l.Receive(MsgId.MISSION_CURRENT, (fun _ -> true), 200)

        match position, current with
        | Some p, c ->
            let p = unbox<MAVLink.mavlink_global_position_int_t> p
            let item = c |> Option.map (fun c -> string (unbox<MAVLink.mavlink_mission_current_t> c).seq) |> Option.defaultValue "?"
            printfn "%s: item %s  %.7f %.7f  %.1f m" l.Name item (float p.lat / 1e7) (float p.lon / 1e7) (float p.relative_alt / 1000.0)
        | None, _ -> ()

    Thread.Sleep 1000
"""

    /// The script for this swarm: the drones' names and connections are its
    /// configuration table; everything else it reads from the exported files.
    let generate (swarm: SwarmMission) : string =
        let drones =
            swarm.Missions
            |> List.map (fun m -> sprintf "        (\"%s\", \"%s\")" m.Drone.Name m.Drone.ConnectionString)
            |> String.concat "\n"

        template.TrimStart('\n').Replace("{{DRONES}}", drones)

    let writeFile (path: string) (swarm: SwarmMission) =
        File.WriteAllText(path, generate swarm)
        printfn "Wrote MAVLink F# show script to: %s" path

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
    QGroundControl.writeSwarmFiles baseDir swarm
    WaypointFile.writeSwarmFiles baseDir swarm
    ParamFile.writeSwarmFiles baseDir swarm
    FsxScript.writeFile (Path.Combine(baseDir, "mavlink_show.fsx")) swarm

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
