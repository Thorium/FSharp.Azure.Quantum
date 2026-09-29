/// MAVLink missions for ArduPilot vehicles: the item types, the file formats a
/// ground station loads (QGroundControl .plan, MAVLink .waypoints, ArduPilot
/// .parm) and the generated F# launcher that uploads, verifies and flies them.
///
/// Shared by the four drone examples: SwarmChoreography exports a synchronised
/// show, FireAirBridge each drone's sorties on the air-bridge lanes,
/// FleetPathPlanning the relay carrier's deployment and the range-split
/// sorties, SwarmTaskAllocation each aircraft's task sorties. What a mission
/// contains is the example's business; this module knows how ArduPilot 4.7
/// flies it (Flight: the timing the examples' tracks use, checked against
/// ArduPilot SITL), which parameters it needs (Params), and how to write it
/// down and fly it (the file formats and the generated launcher).
///
/// Reference: https://mavlink.io/en/
module FSharp.Azure.Quantum.Examples.Drones.MavlinkMission

open System
open System.IO
open System.Text

// =============================================================================
// DOMAIN TYPES
// =============================================================================

/// WGS84 coordinate for real-world positioning
[<Struct>]
type GeoCoordinate =
    {
        Latitude: float // Decimal degrees
        Longitude: float // Decimal degrees
        Altitude: float // Meters above sea level (AMSL) or relative
    }

/// Local NED (North-East-Down) position relative to home
[<Struct>]
type LocalPosition =
    {
        North: float // Meters, positive = north
        East: float // Meters, positive = east
        Down: float // Meters, positive = down (negative = up)
    }

/// The ArduPilot firmware a mission is written for. Parameter names and units
/// follow ArduPilot 4.7, where lengths are metres and speeds m/s; 4.6 and
/// earlier name several of them differently and the launcher refuses a vehicle
/// that lacks one.
type Vehicle =
    /// ArduCopter: multirotor.
    | ArduCopter
    /// ArduPlane with Q_ENABLE = 1: takes off and lands vertically, cruises and
    /// loiters as a fixed-wing.
    | ArduQuadPlane

module Vehicle =
    /// The examples' fleets name fixed-wing models "FixedWing-..."; every
    /// fixed-wing they fly takes off and lands vertically on its own pad, which
    /// only a QuadPlane can.
    let ofModel (model: string) =
        if model.StartsWith("FixedWing", StringComparison.OrdinalIgnoreCase) then
            ArduQuadPlane
        else
            ArduCopter

    let name =
        function
        | ArduCopter -> "ArduCopter"
        | ArduQuadPlane -> "ArduPlane (QuadPlane)"

/// MAVLink command types (subset relevant for choreography)
type MavCmd =
    /// MAV_CMD_NAV_WAYPOINT (16)
    | NavWaypoint
    /// MAV_CMD_NAV_LOITER_TIME (19)
    | NavLoiterTime
    /// MAV_CMD_NAV_RETURN_TO_LAUNCH (20)
    | NavReturnToLaunch
    /// MAV_CMD_NAV_LAND (21)
    | NavLand
    /// MAV_CMD_NAV_TAKEOFF (22)
    | NavTakeoff
    /// MAV_CMD_NAV_LOITER_TO_ALT (31)
    | NavLoiterToAlt
    /// MAV_CMD_NAV_VTOL_TAKEOFF (84)
    | NavVtolTakeoff
    /// MAV_CMD_NAV_VTOL_LAND (85)
    | NavVtolLand
    /// MAV_CMD_NAV_DELAY (93)
    | NavDelay
    /// MAV_CMD_DO_SET_MODE (176)
    | DoSetMode
    /// MAV_CMD_DO_SET_SERVO (183)
    | DoSetServo
    /// MAV_CMD_DO_SET_RELAY (181)
    | DoSetRelay
    /// MAV_CMD_DO_CHANGE_SPEED (178)
    | DoChangeSpeed
    /// MAV_CMD_DO_JUMP (177)
    | DoJump
    /// MAV_CMD_DO_SET_ROI_LOCATION (195)
    | DoSetRoiLocation
    /// Custom for LED control
    | DoSetLedColor

/// MAVLink frame types
type MavFrame =
    /// MAV_FRAME_GLOBAL (0) - WGS84 absolute
    | Global
    /// MAV_FRAME_GLOBAL_RELATIVE_ALT (3) - WGS84 lat/lon, relative alt
    | GlobalRelativeAlt
    /// MAV_FRAME_LOCAL_NED (1) - North-East-Down relative to home
    | LocalNed
    /// MAV_FRAME_LOCAL_ENU (4) - East-North-Up relative to home
    | LocalEnu
    /// MAV_FRAME_MISSION (2) - Mission item specific
    | Mission

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

/// Complete MAVLink mission for a single drone
type DroneMission =
    {
        Drone: MavDroneConfig
        /// The firmware the items and parameters are written for.
        Vehicle: Vehicle
        /// Where this drone arms (its own start slot): ArduPilot's home, so RTL
        /// brings it back here, not to one point shared by the swarm.
        HomePosition: GeoCoordinate
        Items: MissionItem list
        /// ArduPilot parameters the vehicle must carry for the mission to fly as
        /// exported (written to <DroneName>.parm).
        Parameters: (string * float) list
    }

/// One vehicle's mission to fly, with its connection and, for a sequence of
/// sorties on one vehicle, when to start it (seconds after the launcher's T0).
type ScheduledMission =
    {
        Mission: DroneMission
        /// Seconds after T0 at which the launcher starts this mission.
        LaunchS: float
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

// =============================================================================
// MAV COMMAND ENCODING
// =============================================================================

/// Get MAVLink command ID
let mavCmdId =
    function
    | NavWaypoint -> 16
    | NavLoiterTime -> 19
    | NavReturnToLaunch -> 20
    | NavLand -> 21
    | NavTakeoff -> 22
    | NavLoiterToAlt -> 31
    | NavVtolTakeoff -> 84
    | NavVtolLand -> 85
    | NavDelay -> 93
    | DoSetMode -> 176
    | DoSetServo -> 183
    | DoSetRelay -> 181
    | DoChangeSpeed -> 178
    | DoJump -> 177
    | DoSetRoiLocation -> 195
    | DoSetLedColor -> 999 // Custom extension

/// Get MAVLink frame ID
let mavFrameId =
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

/// Jump back to mission item `toSeq` (as numbered in the uploaded mission,
/// where 0 is home) `repeat` more times. ArduPilot: MAV_CMD_DO_JUMP (177).
let doJump (toSeq: int) (repeat: int) (seq: int) : MissionItem =
    { createItem seq DoJump Mission 0.0 0.0 0.0 with
        Param1 = float toSeq
        Param2 = float repeat
    }

/// QuadPlane: climb vertically where it stands to `altitude` (relative), then
/// transition to fixed-wing flight.
let vtolTakeoff (altitude: float) (home: GeoCoordinate) (seq: int) : MissionItem =
    createItem seq NavVtolTakeoff GlobalRelativeAlt home.Latitude home.Longitude altitude

/// QuadPlane: fly to `pos`, transition to hover and descend vertically onto it.
let vtolLand (pos: GeoCoordinate) (seq: int) : MissionItem =
    createItem seq NavVtolLand GlobalRelativeAlt pos.Latitude pos.Longitude 0.0

/// Fixed-wing: circle `pos` clockwise for `seconds` (ArduPilot keeps whole
/// seconds). ArduPlane stores no radius with this item: it circles at
/// WP_LOITER_RAD, which the exported parameters set.
let orbit (seconds: float) (pos: GeoCoordinate) (seq: int) : MissionItem =
    { createItem seq NavLoiterTime GlobalRelativeAlt pos.Latitude pos.Longitude pos.Altitude with
        Param1 = seconds
        Param3 = 1.0
    }

/// Set servo output `channel` to `pwm` (a payload release, for example).
let doSetServo (channel: int) (pwm: int) (seq: int) : MissionItem =
    { createItem seq DoSetServo Mission 0.0 0.0 0.0 with
        Param1 = float channel
        Param2 = float pwm
    }

/// A copter comes to rest at a waypoint only when the waypoint holds: with no
/// hold it carries its speed through the corner onto the next leg, sooner
/// than a model of rest-to-rest legs says. Every copter waypoint therefore
/// holds at least this long, and the examples' tracks count it (Flight.legS
/// does not; the callers add it after each waypoint).
[<Literal>]
let settleS = 1.0

/// Before a hold starts ArduCopter must reach the waypoint, which takes this
/// much longer than the modelled S-curve's end (measured in ArduCopter 4.7.1
/// SITL over 20 legs of 9 m to 3.4 km: 0.6 to 2.0 s, mean 1.2 s).
[<Literal>]
let reachS = 1.2

/// What a copter's stop at a waypoint costs on top of the S-curve leg: reaching
/// it, then the settle hold. The examples' tracks count this; the missions
/// hold settleS.
let stopS = reachS + settleS

/// Items that fly to `pos` and hold there for `holdS` seconds, appended to
/// `items` (sequence numbers are their positions). A waypoint's own hold is
/// whole seconds on ArduPilot, so a fraction follows as a NAV_DELAY.
let holdAt (acceptRadiusM: float) (pos: GeoCoordinate) (holdS: float) (items: ResizeArray<MissionItem>) =
    let holdS = Math.Round(holdS, 3)
    let whole = Math.Floor holdS
    items.Add(waypoint whole acceptRadiusM pos items.Count)

    if holdS - whole > 1e-3 then
        items.Add(navDelay (Math.Round(holdS - whole, 3)) items.Count)

/// A copter waypoint: fly to `pos`, come to rest (settleS), hold `holdS`.
let stopAt (acceptRadiusM: float) (pos: GeoCoordinate) (holdS: float) (items: ResizeArray<MissionItem>) =
    holdAt acceptRadiusM pos (settleS + holdS) items

// =============================================================================
// FLIGHT TIMING AND PARAMETERS (ArduPilot 4.7)
// =============================================================================

/// How the vehicles fly the vertical parts of a mission, as the examples model
/// them and as the exported parameters set them.
module Flight =

    /// Vertical speeds: climb and descent, and the last metres onto the ground
    /// at the slow landing speed.
    [<Struct>]
    type Vertical =
        {
            ClimbMs: float
            DescentMs: float
            LandFinalAltM: float
            LandFinalMs: float
        }

    /// ArduPilot's defaults for both: WP_SPD_UP/DN (Q_WP_SPD_UP/DN), and
    /// LAND_ALT_LOW_M at LAND_SPD_MS (Q_LAND_FINAL_ALT at Q_LAND_FINAL_SPD).
    let vertical =
        function
        | ArduCopter ->
            {
                ClimbMs = 2.5
                DescentMs = 1.5
                LandFinalAltM = 10.0
                LandFinalMs = 0.5
            }
        | ArduQuadPlane ->
            {
                ClimbMs = 2.5
                DescentMs = 1.5
                LandFinalAltM = 6.0
                LandFinalMs = 0.5
            }

    /// A fixed-wing's climb and sink limit in cruise (TECS_CLMB_MAX and
    /// TECS_SINK_MAX defaults).
    [<Literal>]
    let private fixedWingClimbMs = 5.0

    /// Bank limit a fixed-wing turns at (ROLL_LIMIT_DEG allows more).
    [<Literal>]
    let private bankDeg = 45.0

    /// The tightest circle a fixed-wing flies at `cruiseMs`: v² / (g tan bank).
    /// Its loiters use this radius, and it cuts a corner by up to this much.
    let turnRadiusM (cruiseMs: float) =
        Math.Ceiling(cruiseMs * cruiseMs / (9.81 * Math.Tan(bankDeg * Math.PI / 180.0)))

    /// A fixed-wing's flown path through `points` (local metres: x east,
    /// y north, z up), as ArduPlane flies waypoints with WP_RADIUS equal to
    /// its turn radius `radiusM`, rejoining the line between waypoints after
    /// each turn. A turn of up to 90 degrees cuts the corner on an arc
    /// tangent to both legs (a right angle passes 0.4 radius inside the
    /// waypoint). A sharper one starts a radius short of the waypoint, turns on
    /// the tightest circle until parallel to the next leg, which leaves it
    /// outside that leg, and rejoins it two radii on. `heading` is the
    /// direction it already flies at the first point (east, north), or none
    /// when it starts from a hover; then it first turns to head for the second
    /// point. Returns the path (arcs every 10 degrees; height changing evenly
    /// between the points where waypoints count as reached) and, for each
    /// point, where on the path it counts as reached.
    let fixedWingPath
        (radiusM: float)
        (heading: (float * float) option)
        (points: (float * float * float) list)
        : (float * float * float)[] * int[] =
        let pts = Array.ofList points
        let n = pts.Length
        let path = ResizeArray<float * float>()
        let reached = Array.zeroCreate n
        let at k = let x, y, _ = pts.[k] in (x, y)
        let add (ax: float, ay: float) (bx, by) = (ax + bx, ay + by)
        let sub (ax: float, ay: float) (bx, by) = (ax - bx, ay - by)
        let scale (s: float) (x, y) = (s * x, s * y)
        let length (x: float, y) = Math.Sqrt(x * x + y * y)
        let dot (ax: float, ay) (bx, by) = ax * bx + ay * by

        let unit v =
            let l = length v
            if l > 1e-9 then Some(scale (1.0 / l) v) else None

        let normal left (x: float, y: float) = if left then (-y, x) else (y, -x)

        // Points of the arc around `centre` from where the path now ends,
        // `sweep` radians to the left or right.
        let arc (centre: float * float) (sweep: float) (left: bool) =
            let rx, ry = sub path.[path.Count - 1] centre
            let r = length (rx, ry)
            let start = Math.Atan2(ry, rx)
            let steps = max 1 (int (Math.Ceiling(sweep / (10.0 * Math.PI / 180.0))))

            for k in 1..steps do
                let a = start + (if left then 1.0 else -1.0) * sweep * float k / float steps
                path.Add(add centre (r * Math.Cos a, r * Math.Sin a))

        path.Add(at 0)

        // Already flying at the first point: turn until heading for the second.
        match heading, (if n > 1 then Some(at 1) else None) with
        | Some u, Some target ->
            let p = at 0
            let left = fst u * snd (sub target p) - snd u * fst (sub target p) > 0.0
            let centre = add p (scale radiusM (normal left u))
            let d = length (sub target centre)

            if d > radiusM * 1.0001 then
                let tx, ty = sub target centre
                let beta = Math.Atan2(ty, tx)
                let alpha = Math.Acos(radiusM / d)
                let px, py = sub p centre
                let start = Math.Atan2(py, px)
                let finish = if left then beta - alpha else beta + alpha
                let twoPi = 2.0 * Math.PI
                let s = if left then finish - start else start - finish
                arc centre (((s % twoPi) + twoPi) % twoPi) left
        | _ -> ()

        for k in 1 .. n - 1 do
            let c = at k

            if k = n - 1 then
                path.Add c
                reached.[k] <- path.Count - 1
            else
                let from = path.[path.Count - 1]
                let next = at (k + 1)

                match unit (sub c from), unit (sub next c) with
                | Some u1, Some u2 ->
                    let cross = fst u1 * snd u2 - snd u1 * fst u2
                    let turn = abs (Math.Atan2(cross, dot u1 u2))
                    let left = cross > 0.0
                    let inM, outM = length (sub c from), length (sub next c)

                    if turn < 2.0 * Math.PI / 180.0 then
                        path.Add c
                        reached.[k] <- path.Count - 1
                    elif turn <= Math.PI / 2.0 then
                        // Tangent to both legs, as tight as the legs allow.
                        let d = min (radiusM * Math.Tan(turn / 2.0)) (0.5 * min inM outM)
                        let r = d / Math.Tan(turn / 2.0)
                        let t1 = sub c (scale d u1)
                        path.Add t1
                        reached.[k] <- path.Count - 1
                        arc (add t1 (scale r (normal left u1))) turn left
                    else
                        // Starts a radius short, turns until parallel to the
                        // next leg, then rejoins it two radii on.
                        let s = sub c (scale (min radiusM (0.5 * inM)) u1)
                        path.Add s
                        reached.[k] <- path.Count - 1
                        arc (add s (scale radiusM (normal left u1))) turn left
                        let along = dot (sub path.[path.Count - 1] c) u2
                        path.Add(add c (scale (min (max along 0.0 + 2.0 * radiusM) (0.9 * outM)) u2))
                | _ ->
                    path.Add c
                    reached.[k] <- path.Count - 1

        // Heights: from where each point counts as reached to the next's,
        // evenly along the path.
        let along =
            path
            |> Seq.pairwise
            |> Seq.scan (fun s (a, b) -> s + length (sub b a)) 0.0
            |> Array.ofSeq

        let z (_, _, h) = h
        let heights = Array.zeroCreate path.Count

        for i in 0 .. reached.[0] do
            heights.[i] <- z pts.[0]

        for k in 1 .. n - 1 do
            let a, b = reached.[k - 1], reached.[k]

            for i in a..b do
                let span = along.[b] - along.[a]
                let f = if span > 1e-9 then (along.[i] - along.[a]) / span else 1.0
                heights.[i] <- z pts.[k - 1] + f * (z pts.[k] - z pts.[k - 1])

        (Array.init path.Count (fun i -> let x, y = path.[i] in (x, y, heights.[i])), reached)

    /// ArduCopter's waypoint acceleration and jerk limits (4.7 defaults:
    /// WP_ACC, WP_ACC_Z, WP_JERK, PSC_D_JERK). With a waypoint radius of a few
    /// metres or less a copter comes (nearly) to rest at every waypoint, so
    /// every leg is flown from rest to rest: an S-curve, not constant speed.
    /// Checked against ArduCopter 4.7.1 SITL: within a second per leg.
    [<Literal>]
    let accelMss = 2.5

    [<Literal>]
    let accelZMss = 1.0

    [<Literal>]
    let jerkMsss = 1.0

    [<Literal>]
    let jerkZMsss = 5.0

    /// From MISSION_START to the take-off climb under way: the motors spool up
    /// and the throttle ramps until the vehicle lifts. Measured in ArduCopter
    /// 4.7.1 SITL: 3 to 4.5 s.
    [<Literal>]
    let spoolUpS = 4.0

    /// One straight leg flown from rest to rest.
    type Leg =
        {
            LengthM: float
            /// Top speed along the track (a short leg never reaches its limit).
            PeakMs: float
            /// Seconds of each ramp (from rest to the peak, and back).
            RampS: float
            TotalS: float
            AccelMss: float
            JerkMsss: float
        }

    /// Time and distance to go from rest to `v` with acceleration up to `a` and
    /// jerk up to `j`: a jerk-limited S-curve, symmetric, so the distance is
    /// v * time / 2.
    let private rampTo (v: float) (a: float) (j: float) =
        let t =
            if v <= a * a / j then
                2.0 * Math.Sqrt(v / j)
            else
                v / a + a / j

        (t, v * t / 2.0)

    /// Distance covered `t` seconds into a ramp from rest to `v` (closed form:
    /// jerk +j, then constant acceleration if `a` is reached, then jerk -j).
    let private rampDistance (v: float) (a: float) (j: float) (t: float) =
        let ramp, _ = rampTo v a j
        let tj = if v <= a * a / j then ramp / 2.0 else a / j
        let t = max 0.0 (min ramp t)
        let peakAcc = j * tj
        // End of the rising-jerk phase
        let s1 = j * tj ** 3.0 / 6.0
        let v1 = j * tj * tj / 2.0
        let flat = ramp - 2.0 * tj

        if t <= tj then
            j * t ** 3.0 / 6.0
        elif t <= tj + flat then
            let u = t - tj
            s1 + v1 * u + peakAcc * u * u / 2.0
        else
            let s2 = s1 + v1 * flat + peakAcc * flat * flat / 2.0
            let v2 = v1 + peakAcc * flat
            let u = t - tj - flat
            s2 + v2 * u + peakAcc * u * u / 2.0 - j * u ** 3.0 / 6.0

    /// A copter leg of `horizontalM` and `dzM` (up positive) at horizontal speed
    /// `speedMs`, climbing no faster than `climbMs` and descending no faster
    /// than `descentMs`. Along a sloped track each axis's limits are divided by
    /// that axis's share of the track.
    let copterLeg (speedMs: float) (climbMs: float) (descentMs: float) (horizontalM: float) (dzM: float) =
        let length = Math.Sqrt(horizontalM * horizontalM + dzM * dzM)

        if length < 1e-9 then
            {
                LengthM = 0.0
                PeakMs = 0.0
                RampS = 0.0
                TotalS = 0.0
                AccelMss = accelMss
                JerkMsss = jerkMsss
            }
        else
            let ch = horizontalM / length
            let cz = abs dzM / length

            let limit (h: float) (z: float) =
                min
                    (if ch > 1e-9 then h / ch else Double.PositiveInfinity)
                    (if cz > 1e-9 then z / cz else Double.PositiveInfinity)

            let vmax = limit speedMs (if dzM >= 0.0 then climbMs else descentMs)
            let a = limit accelMss accelZMss
            let j = limit jerkMsss jerkZMsss
            let ramp, rampM = rampTo vmax a j

            if 2.0 * rampM <= length then
                {
                    LengthM = length
                    PeakMs = vmax
                    RampS = ramp
                    TotalS = length / vmax + ramp
                    AccelMss = a
                    JerkMsss = j
                }
            else
                // The peak speed whose two ramps just cover the leg.
                let rec peak lo hi i =
                    let mid = (lo + hi) / 2.0

                    if i = 0 then
                        mid
                    elif 2.0 * snd (rampTo mid a j) > length then
                        peak lo mid (i - 1)
                    else
                        peak mid hi (i - 1)

                let v = peak 0.0 vmax 50
                let r, _ = rampTo v a j

                {
                    LengthM = length
                    PeakMs = v
                    RampS = r
                    TotalS = 2.0 * r
                    AccelMss = a
                    JerkMsss = j
                }

    /// Distance along the leg `t` seconds after it started.
    let alongM (leg: Leg) (t: float) =
        if leg.LengthM <= 0.0 || t <= 0.0 then
            0.0
        elif t >= leg.TotalS then
            leg.LengthM
        else
            let rampM = rampDistance leg.PeakMs leg.AccelMss leg.JerkMsss leg.RampS

            if t <= leg.RampS then
                rampDistance leg.PeakMs leg.AccelMss leg.JerkMsss t
            elif t >= leg.TotalS - leg.RampS then
                leg.LengthM - rampDistance leg.PeakMs leg.AccelMss leg.JerkMsss (leg.TotalS - t)
            else
                rampM + leg.PeakMs * (t - leg.RampS)

    /// Where a leg's position is worth a sample, as (seconds from its start,
    /// fraction of its length): eight points through each ramp, where the speed
    /// changes. Between the ramps the speed is constant, so a straight line
    /// between samples is exact there.
    let legFractions (leg: Leg) =
        if leg.LengthM <= 0.0 || leg.TotalS <= 0.0 then
            []
        else
            let r = leg.RampS

            [ for k in 1..8 -> float k * r / 8.0 ]
            @ [ for k in 0..7 -> leg.TotalS - r + float k * r / 8.0 ]
            |> List.filter (fun t -> t > 1e-9 && t < leg.TotalS - 1e-9)
            |> List.distinct
            |> List.sort
            |> List.map (fun t -> (t, alongM leg t / leg.LengthM))

    /// Seconds a copter needs to get `distanceM` from rest on a level leg at
    /// `speedMs` (or, run backwards, to come to rest from that far out).
    let clearS (speedMs: float) (distanceM: float) =
        let long = copterLeg speedMs speedMs speedMs (1e6 + 2.0 * distanceM) 0.0

        let rec find lo hi i =
            let mid = (lo + hi) / 2.0

            if i = 0 then hi
            elif alongM long mid >= distanceM then find lo mid (i - 1)
            else find mid hi (i - 1)

        find 0.0 (long.RampS + distanceM / max 1e-6 long.PeakMs) 50

    /// Seconds for a straight leg of `horizontalM` and `dzM` (up positive) at
    /// `cruiseMs`. A copter flies it from rest to rest (copterLeg); a QuadPlane
    /// cruises it as a fixed-wing at constant speed, no steeper than its climb
    /// and sink limits, and climbs or descends vertically in hover.
    let legS (vehicle: Vehicle) (cruiseMs: float) (horizontalM: float) (dzM: float) =
        let v = vertical vehicle

        match vehicle with
        | ArduCopter -> (copterLeg cruiseMs v.ClimbMs v.DescentMs horizontalM dzM).TotalS
        | ArduQuadPlane when horizontalM < 1.0 -> (copterLeg cruiseMs v.ClimbMs v.DescentMs 0.0 dzM).TotalS
        | ArduQuadPlane ->
            let straight = Math.Sqrt(horizontalM * horizontalM + dzM * dzM) / cruiseMs
            max straight (abs dzM / fixedWingClimbMs)

    /// Seconds for a vertical landing from `altM`, starting from rest: the
    /// descent speed down to the final altitude, then the landing speed onto
    /// the ground (with the ramp up to the first).
    let landingAtS (vehicle: Vehicle) (descentMs: float) (altM: float) =
        let v = vertical vehicle
        let firstMs = if altM > v.LandFinalAltM then descentMs else v.LandFinalMs
        let ramp, _ = rampTo firstMs accelZMss jerkZMsss

        max 0.0 (altM - v.LandFinalAltM) / descentMs
        + min altM v.LandFinalAltM / v.LandFinalMs
        + ramp / 2.0

    let landingS (vehicle: Vehicle) (altM: float) =
        landingAtS vehicle (vertical vehicle).DescentMs altM

    /// A QuadPlane's VTOL landing slows down on the way in and settles over
    /// the landing point before it descends: its cruise leg to that point
    /// takes this much longer. Measured in ArduPlane 4.7.1 SITL over three
    /// landings at 20 m/s: 11 to 12 s over the last 1.5 km.
    [<Literal>]
    let vtolApproachS = 12.0

    /// Seconds to get from rest to `speedMs` on a level leg (and back to rest).
    let rampS (speedMs: float) = fst (rampTo speedMs accelMss jerkMsss)

    /// How long a landed vehicle stays down before the next sortie can start:
    /// touchdown to disarm (4 to 5 s in SITL), then the launcher uploads the
    /// sortie, reads it back and arms.
    [<Literal>]
    let turnaroundS = 15.0

    /// How late a sortie may start (the launcher refuses it after that), and
    /// how far an open-loop mission's timing may drift, either way, per leg
    /// and per second flown. Every booking of a shared place is widened by
    /// what these allow. Measured in ArduCopter 4.7.1 SITL against this
    /// model: short 1 m/s legs up to 0.23 s early each, 10 m/s legs of a few
    /// kilometres 1.5% early.
    [<Literal>]
    let startSlackS = 2.0

    [<Literal>]
    let legSlackS = 0.3

    [<Literal>]
    let driftPerS = 0.02

    /// How late a point may be reached `sinceLaunchS` and `legs` into a sortie.
    let lateAt (sinceLaunchS: float) (legs: int) =
        startSlackS + legSlackS * float legs + driftPerS * max 0.0 sinceLaunchS

    /// How early a point may be reached `sinceLaunchS` and `legs` into a sortie.
    let earlyAt (sinceLaunchS: float) (legs: int) =
        legSlackS * float legs + driftPerS * max 0.0 sinceLaunchS

/// What a vehicle does when the ground station's link is lost in AUTO.
type LostLink =
    /// Fly on: the mission itself is the modelled recovery.
    | ContinueMission
    /// Return to launch (the modelled fallback).
    | ReturnHome

/// What one vehicle's parameters must say for its mission to fly as modelled.
type Profile =
    {
        Vehicle: Vehicle
        /// Speed of the mission's legs, m/s.
        CruiseMs: float
        /// Vertical speeds of take-off and landing, m/s (Flight.vertical has
        /// ArduPilot's defaults).
        ClimbMs: float
        DescentMs: float
        /// Altitude its return to launch flies at, m relative to home.
        RtlAltM: float
        /// When a waypoint counts as reached, m.
        WaypointRadiusM: float
        LostLink: LostLink
    }

/// ArduPilot 4.7 parameters, by the names and units 4.7 uses.
module Params =

    [<Literal>]
    let disarmDelayS = 5.0

    [<Literal>]
    let gcsTimeoutS = 5.0

    let copter (p: Profile) : (string * float) list =
        let v = Flight.vertical ArduCopter

        [
            "RTL_ALT_M", p.RtlAltM
            "RTL_ALT_FINAL_M", 0.0 // land at home
            // No cone: RTL climbs to RTL_ALT_M (it never descends to it) however
            // near home it starts.
            "RTL_CONE_SLOPE", 0.0
            "RTL_LOIT_TIME", 0.0
            "DISARM_DELAY", disarmDelayS
            // The launcher arms in GUIDED and starts the mission from the
            // ground; with no pilot on the throttle, AUTO takes off only with
            // bit 1 (take off without raising the throttle).
            "AUTO_OPTIONS", 2.0
            "LAND_ALT_LOW_M", v.LandFinalAltM
            "LAND_SPD_MS", v.LandFinalMs
            "LAND_SPD_HIGH_MS", 0.0 // = WP_SPD_DN
            "WP_SPD", p.CruiseMs
            "WP_SPD_UP", p.ClimbMs
            "WP_SPD_DN", p.DescentMs
            // The S-curve limits every leg's modelled timing assumes.
            "WP_ACC", Flight.accelMss
            "WP_ACC_Z", Flight.accelZMss
            "WP_JERK", Flight.jerkMsss
            "PSC_D_JERK", Flight.jerkZMsss
            "WP_RADIUS_M", p.WaypointRadiusM
            "FS_GCS_ENABLE", 1.0
            "FS_GCS_TIMEOUT", gcsTimeoutS
            "FS_THR_ENABLE", 1.0
            // In AUTO: continue on RC loss (bit 0) and on GCS loss (bit 1) when
            // the mission is the recovery; always continue a landing (bit 3).
            "FS_OPTIONS",
            (match p.LostLink with
             | ContinueMission -> 11.0
             | ReturnHome -> 8.0)
        ]
        @ (match p.LostLink with
           // Low battery warns only: the sortie already fits endurance minus
           // reserve, and a lone RTL would cut across the others.
           | ContinueMission -> [ "BATT_FS_LOW_ACT", 0.0 ]
           // The modelled fallback: home when it can (RTL on low battery),
           // a controlled descent in place when it cannot (land on critical).
           | ReturnHome -> [ "BATT_FS_LOW_ACT", 2.0; "BATT_FS_CRT_ACT", 1.0 ])

    let quadPlane (p: Profile) : (string * float) list =
        let v = Flight.vertical ArduQuadPlane

        [
            "AIRSPEED_CRUISE", p.CruiseMs
            "RTL_ALTITUDE", p.RtlAltM
            "RTL_AUTOLAND", 0.0
            // RTL flies home as a fixed-wing at RTL_ALTITUDE, then lands
            // vertically on it (QRTL).
            "Q_RTL_MODE", 1.0
            "Q_RTL_ALT", p.RtlAltM
            "Q_WP_SPD_UP", p.ClimbMs
            "Q_WP_SPD_DN", p.DescentMs
            "Q_LAND_FINAL_ALT", v.LandFinalAltM
            "Q_LAND_FINAL_SPD", v.LandFinalMs
            "WP_RADIUS", p.WaypointRadiusM
            "WP_LOITER_RAD", Flight.turnRadiusM p.CruiseMs
            "LAND_DISARMDELAY", disarmDelayS
            "FS_GCS_ENABL", 1.0
            "FS_LONG_TIMEOUT", gcsTimeoutS
            "FS_SHORT_ACTN", 0.0 // in AUTO: carry on
            "FS_LONG_ACTN",
            (match p.LostLink with
             | ContinueMission -> 0.0
             | ReturnHome -> 1.0)
            "THR_FAILSAFE", 1.0
        ]

    let forProfile (p: Profile) =
        match p.Vehicle with
        | ArduCopter -> copter p
        | ArduQuadPlane -> quadPlane p

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

        sb.AppendLine "  \"geoFence\": { \"circles\": [], \"polygons\": [], \"version\": 2 },"
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
        // MAV_TYPE: 2 = quadrotor, 20 = VTOL quadrotor (a QuadPlane); the
        // launcher picks the flight modes from it.
        sb.AppendLine(
            sprintf
                "    \"vehicleType\": %d,"
                (match mission.Vehicle with
                 | ArduCopter -> 2
                 | ArduQuadPlane -> 20)
        )
        |> ignore

        sb.AppendLine("    \"version\": 2") |> ignore
        sb.AppendLine("  },") |> ignore

        // Rally points (empty)
        sb.AppendLine "  \"rallyPoints\": { \"points\": [], \"version\": 2 }," |> ignore

        sb.AppendLine("  \"version\": 1") |> ignore
        sb.AppendLine("}") |> ignore

        sb.ToString()

    /// Write mission to QGroundControl .plan file
    let writeFile (path: string) (mission: DroneMission) =
        File.WriteAllText(path, toJson mission)
        printfn "Wrote QGroundControl plan to: %s" path

    /// Write all drone missions to separate .plan files
    let writeAll (baseDir: string) (missions: DroneMission list) =
        Directory.CreateDirectory(baseDir) |> ignore

        for mission in missions do
            let filename = $"%s{mission.Drone.Name}_mission.plan"
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
    let writeAll (baseDir: string) (missions: DroneMission list) =
        Directory.CreateDirectory(baseDir) |> ignore

        for mission in missions do
            let filename = $"%s{mission.Drone.Name}.waypoints"
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

        sb.AppendLine(
            sprintf
                "# %s: failsafe and speed parameters for the exported mission, %s 4.7 names and units"
                mission.Drone.Name
                (Vehicle.name mission.Vehicle)
        )
        |> ignore

        sb.AppendLine "# Load before flight and verify on the vehicle; the mission assumes these values."
        |> ignore

        for name, value in mission.Parameters do
            sb.AppendLine(sprintf "%s %s" name (value.ToString("R", Globalization.CultureInfo.InvariantCulture)))
            |> ignore

        sb.ToString()

    /// Parse a parameter file back (the evidence pack checks what was written).
    let parse (text: string) : Map<string, float> =
        text.Split '\n'
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

    let fileName (mission: DroneMission) = $"%s{mission.Drone.Name}.parm"

    let writeAll (baseDir: string) (missions: DroneMission list) =
        Directory.CreateDirectory(baseDir) |> ignore

        for mission in missions do
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
// mavlink_show.fsx - generated by FSharp.Azure.Quantum.
//
// Flies the exported missions over MAVLink 2 on ArduPilot 4.7 vehicles:
// ArduCopter, and ArduPlane QuadPlanes (vertical take-off and landing).
// TRY IT AGAINST ARDUPILOT SITL BEFORE ANY REAL AIRCRAFT.
//
//   dotnet fsi mavlink_show.fsx --dry-run   loads the files, builds every message,
//                                           prints what it would send; no network
//   dotnet fsi mavlink_show.fsx             connects and flies
//   dotnet fsi mavlink_show.fsx --speedup N against SITL run at N x real time
//                                           (sim_vehicle.py --speedup N): every
//                                           time here is in the vehicles' seconds
//   dotnet fsi mavlink_show.fsx --check     compares telemetry.csv of a flight
//                                           with plan_tracks.csv, draws
//                                           flight.svg; no network
//   dotnet fsi mavlink_show.fsx --draw      draws plan.svg (or flight.svg after a
//                                           flight); no network
//
// Per vehicle it connects, checks the vehicle is the kind the mission is for,
// sets every parameter from <Name>.parm and reads each one back, uploads
// <Name>_mission.plan and reads the whole mission back, and checks the vehicle
// stands on its declared home (its own slot). It REFUSES to start unless every
// vehicle carries exactly the mission and parameters the evidence pack checked.
// Then it arms and starts every mission due at T0 at the same moment. A vehicle
// with several missions (sorties) gets each later one uploaded, verified and
// started at its planned time, once it has landed and disarmed.
//
// It prints telemetry and writes it to telemetry.csv (seconds after T0), and
// exits once every sortie has flown and every vehicle is down and disarmed.
// Ctrl+C sends every vehicle RTL (the modelled abort).
//
// MAVLink library: the NuGet package "MAVLink" (ArduPilot Mission Planner's
// generated C# MAVLink, https://github.com/ArduPilot/MissionPlanner/tree/master/ExtLibs/Mavlink).

#r "nuget: MAVLink, 1.0.8"

// Vector drawing for the flight pictures (--draw, and after a flight).
#r "nuget: VectSharp.SVG, 1.10.2"
#r "nuget: VectSharp.ThreeD, 1.1.3"

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

/// Mission name (as in the exported files), connection, and seconds after T0 at
/// which it starts. Several entries on one connection are that vehicle's
/// sorties, flown in order of their start times.
///   "tcp:HOST:PORT"   e.g. SITL instance i serves tcp:127.0.0.1:(5760 + 10 i)
///   "udpin:PORT"      listen for a vehicle or MAVProxy --out=udp:127.0.0.1:PORT
let drones =
    [
{{DRONES}}
    ]

/// How far (m) a vehicle may stand from its declared home before arming.
let homeToleranceM = 2.0

/// How late (s) a sortie may start. The plan was checked for its planned
/// time (and FireAirBridge booked its shared places with this much slack);
/// later than this it is not flown.
let lateToleranceS = {{LATE}}

/// How long (s) a vehicle may keep refusing to arm at T0 (pre-arm checks, such
/// as the EKF settling) before the launcher gives up without flying anyone.
let armWaitS = 90.0

let dryRun = fsi.CommandLineArgs |> Array.contains "--dry-run"

/// SITL only: the simulation runs this many times faster than the wall clock,
/// so the launcher's clock, its heartbeat and its polling run that much faster.
let speedup =
    match fsi.CommandLineArgs |> Array.tryFindIndex ((=) "--speedup") with
    | Some i when i + 1 < fsi.CommandLineArgs.Length -> max 1.0 (float fsi.CommandLineArgs.[i + 1])
    | _ -> 1.0

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

type Sortie =
    {
        Name: string
        Connection: string
        LaunchS: float
        Home: float[]
        /// The plan's MAV_TYPE: 2 = quadrotor (ArduCopter), 20 = VTOL (a QuadPlane).
        VehicleType: int
        Mission: Item list
        Params: (string * float) list
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

    (home, mission.GetProperty("vehicleType").GetInt32(), items)

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
// VEHICLE KINDS
// =============================================================================

let isPlane (vehicleType: int) = vehicleType <> 2

let kindName (vehicleType: int) =
    if isPlane vehicleType then "ArduPlane QuadPlane" else "ArduCopter"

/// Heartbeat MAV_TYPEs each firmware reports: a copter mission is never flown by
/// a plane, or the reverse.
let copterTypes = set [ 2uy; 3uy; 4uy; 13uy; 14uy; 15uy; 29uy ]
let planeTypes = set [ 1uy; 19uy; 20uy; 21uy; 22uy; 23uy; 24uy; 25uy ]

// Custom modes. ArduCopter arms in GUIDED (4) and returns in RTL (6). ArduPlane
// arms in QLOITER (19) and returns in RTL (11), which with Q_RTL_MODE = 1 ends
// in a vertical landing at home. MISSION_START switches either to AUTO.
let armMode vehicleType = if isPlane vehicleType then 19.0 else 4.0
let rtlMode vehicleType = if isPlane vehicleType then 11.0 else 6.0

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

/// Commands whose latitude, longitude and altitude are a position.
let isPositional cmd =
    cmd = 16 || cmd = 19 || cmd = 21 || cmd = 22 || cmd = 84 || cmd = 85

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

type MsgId = MAVLink.MAVLINK_MSG_ID

/// The autopilot's own heartbeat: not a ground station's (MAV_TYPE_GCS), and
/// not a peripheral's: a gimbal, camera or companion computer reports
/// MAV_AUTOPILOT_INVALID, and ArduPilot's autopilot is component 1.
let isAutopilotHeartbeat (m: MAVLink.MAVLinkMessage) =
    m.msgid = uint32 MsgId.HEARTBEAT
    && (let h = unbox<MAVLink.mavlink_heartbeat_t> m.data
        h.``type`` <> 6uy && h.autopilot <> 8uy && m.compid = 1uy)

/// One vehicle connection: a reader thread fills the inbox for request/reply
/// exchanges and keeps the latest message of each kind for telemetry.
type Link(name: string, connection: string) =
    let inbox = new BlockingCollection<MAVLink.MAVLinkMessage>()
    let latest = ConcurrentDictionary<uint32, obj * DateTime>()
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
                        | m ->
                            // Only the autopilot's heartbeat tells whether it is armed.
                            let vehicle = m.msgid <> uint32 MsgId.HEARTBEAT || isAutopilotHeartbeat m

                            if vehicle then
                                latest.[m.msgid] <- (m.data, DateTime.UtcNow)

                            inbox.Add m
                    with
                    | :? TimeoutException -> ()
                    // Whatever breaks, the reader keeps the launcher alive: the
                    // vehicles fly their AUTO missions on regardless.
                    | ex ->
                        eprintfn "%s: link error: %s" name ex.Message
                        Thread.Sleep 500),
            IsBackground = true
        )

    do thread.Start()

    let take (timeoutMs: int) =
        let mutable m: MAVLink.MAVLinkMessage = null
        if inbox.TryTake(&m, timeoutMs) then Some m else None

    member val Target = 1uy with get, set
    member _.Name = name

    /// Wait for the vehicle's heartbeat, talk to its system id from then on, and
    /// return the MAV_TYPE it reports.
    member this.WaitHeartbeat(timeoutMs: int) =
        let until = DateTime.UtcNow.AddMilliseconds(float timeoutMs)

        let rec next () =
            let left = int (until - DateTime.UtcNow).TotalMilliseconds

            if left <= 0 then
                None
            else
                match take left with
                | Some m when isAutopilotHeartbeat m ->
                    this.Target <- m.sysid
                    Some (unbox<MAVLink.mavlink_heartbeat_t> m.data).``type``
                | Some _ -> next ()
                | None -> None

        next ()

    member _.Send(msgid: MsgId, payload: obj) =
        lock sendLock (fun () ->
            let bytes = writer.GenerateMAVLinkPacket20(msgid, payload, false, gcsSystem, gcsComponent, seq)
            seq <- (seq + 1) % 256
            send bytes)

    /// The next message with this id that satisfies `accept`, or None.
    member _.Receive(msgid: MsgId, accept: obj -> bool, timeoutMs: int) =
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

    /// The next message of any of these kinds, with its id, or None.
    member _.ReceiveAny(msgids: MsgId list, timeoutMs: int) =
        let wanted = msgids |> List.map uint32 |> Set.ofList
        let until = DateTime.UtcNow.AddMilliseconds(float timeoutMs)

        let rec next () =
            let left = int (until - DateTime.UtcNow).TotalMilliseconds

            if left <= 0 then
                None
            else
                match take left with
                | Some m when wanted.Contains m.msgid -> Some(m.msgid, m.data)
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
            | None -> failwithf "no %A reply to %A" reply msgid

        go (defaultArg tries 5)

    /// The latest message of this kind and when it arrived.
    member _.Latest(msgid: MsgId) =
        match latest.TryGetValue(uint32 msgid) with
        | true, (d, at) -> Some(d, at)
        | _ -> None

    /// Drop queued messages nobody asked for (telemetry reads `Latest`).
    member _.Drain() =
        let mutable m: MAVLink.MAVLinkMessage = null

        while inbox.TryTake(&m) do
            ()

let heartbeat () =
    // custom_mode, type (MAV_TYPE_GCS), autopilot (MAV_AUTOPILOT_INVALID),
    // base_mode, system_status (MAV_STATE_ACTIVE), mavlink_version
    MAVLink.mavlink_heartbeat_t(0u, 6uy, 8uy, 0uy, 4uy, 3uy)

let isParam (name: string) (d: obj) =
    paramName (unbox<MAVLink.mavlink_param_value_t> d).param_id = name

/// Set a parameter; Error when the vehicle has no parameter by that name.
let setParam (link: Link) (name: string) (value: float) =
    let msg = MAVLink.mavlink_param_set_t(float32 value, link.Target, 1uy, paramId name, 9uy) // REAL32

    try
        link.Request(MsgId.PARAM_SET, msg, MsgId.PARAM_VALUE, isParam name, 3) |> ignore
        Ok()
    with _ ->
        Error(sprintf "%s: the vehicle has no parameter %s (the files use ArduPilot 4.7 names)" link.Name name)

let readParam (link: Link) (name: string) =
    let msg = MAVLink.mavlink_param_request_read_t(-1s, link.Target, 1uy, paramId name)

    try
        let reply = link.Request(MsgId.PARAM_REQUEST_READ, msg, MsgId.PARAM_VALUE, isParam name, 3)
        Ok(float (unbox<MAVLink.mavlink_param_value_t> reply).param_value)
    with _ ->
        Error(sprintf "%s: no value for parameter %s" link.Name name)

let uploadMission (link: Link) (mission: Item list) =
    let items = Array.ofList mission
    link.Drain()
    let count = MAVLink.mavlink_mission_count_t(uint16 items.Length, link.Target, 1uy, 0uy)
    link.Send(MsgId.MISSION_COUNT, count)

    let rec serve () =
        // ArduPilot asks with MISSION_REQUEST (deprecated, still what it sends)
        // or MISSION_REQUEST_INT; either is answered with MISSION_ITEM_INT.
        let request =
            match link.ReceiveAny([ MsgId.MISSION_REQUEST_INT; MsgId.MISSION_REQUEST ], 3000) with
            | Some(id, d) when id = uint32 MsgId.MISSION_REQUEST_INT ->
                Some(int (unbox<MAVLink.mavlink_mission_request_int_t> d).seq)
            | Some(_, d) -> Some(int (unbox<MAVLink.mavlink_mission_request_t> d).seq)
            | None -> None

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
/// (seq 0) is left out: ArduPilot sets it where the vehicle arms. ArduPilot
/// keeps a hold or loiter time in whole seconds.
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
                        | 16
                        | 19 -> near (float a.param1) (Math.Floor e.P.[0]) 1e-6
                        | 178 -> near (float a.param1) e.P.[0] 1e-6 && near (float a.param2) e.P.[1] 1e-4
                        | 93 -> near (float a.param1) e.P.[0] 1e-3
                        | 177
                        | 183 -> near (float a.param1) e.P.[0] 1e-6 && near (float a.param2) e.P.[1] 1e-6
                        | _ -> true

                    let position =
                        match e.Command with
                        | 16
                        | 19
                        | 21
                        | 85 ->
                            abs (a.x - int (Math.Round(e.P.[4] * 1e7))) <= 1
                            && abs (a.y - int (Math.Round(e.P.[5] * 1e7))) <= 1
                            && near (float a.z) e.P.[6] 0.01
                        | 22
                        | 84 -> near (float a.z) e.P.[6] 0.01
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

/// A command, and whether the vehicle accepted it.
/// A command sent up to `tries` times until acknowledged, and the result.
let tryCommandTries (tries: int) (link: Link) (cmd: int) (p: float list) =
    try
        let reply =
            link.Request(
                MsgId.COMMAND_LONG,
                commandLong link.Target cmd p,
                MsgId.COMMAND_ACK,
                (fun d -> int (unbox<MAVLink.mavlink_command_ack_t> d).command = cmd),
                tries
            )

        match (unbox<MAVLink.mavlink_command_ack_t> reply).result with
        | 0uy -> Ok()
        | r -> Error(sprintf "%s: command %d refused (MAV_RESULT %d)" link.Name cmd r)
    with ex ->
        Error(sprintf "%s: command %d: %s" link.Name cmd ex.Message)

let tryCommand (link: Link) (cmd: int) (p: float list) = tryCommandTries 5 link cmd p

let command (link: Link) (cmd: int) (p: float list) =
    match tryCommand link cmd p with
    | Ok() -> ()
    | Error e -> failwith e

let distanceM (lat1: float) (lon1: float) (lat2: float) (lon2: float) =
    let r = 6371000.0
    let dLat = (lat2 - lat1) * Math.PI / 180.0
    let dLon = (lon2 - lon1) * Math.PI / 180.0 * Math.Cos(lat1 * Math.PI / 180.0)
    r * Math.Sqrt(dLat * dLat + dLon * dLon)

// =============================================================================
// CHECK AND DRAW: the flight against the plan, and both as a picture
// =============================================================================

open VectSharp
open VectSharp.SVG
open VectSharp.ThreeD

let invariant = Globalization.CultureInfo.InvariantCulture
let number (s: string) = Double.Parse(s, invariant)

/// A CSV file's column lookup and rows.
let readCsv (path: string) =
    let lines = File.ReadAllLines path |> Array.filter (fun l -> l.Trim() <> "")
    let header = lines.[0].Split(',')
    let col (name: string) = Array.IndexOf(header, name)
    (col, lines |> Array.skip 1 |> Array.map (fun l -> l.Split(',')))

/// One mission's track: (seconds after T0, east m, north m, height m), by time.
type Track = (float * float * float * float)[]

/// The aircraft a mission belongs to: its name without a trailing _s<n>.
let aircraftOf (mission: string) =
    let m = Text.RegularExpressions.Regex.Match(mission, "^(.*)_s\\d+$")
    if m.Success then m.Groups.[1].Value else mission

/// Below this height an aircraft counts as on the ground.
let airborneM = 0.3

/// The planned tracks (plan_tracks.csv) and the flown ones (telemetry.csv, by
/// sortie), whichever exist, in metres around the plan's centre.
let loadTracks () =
    let read (file: string) (mission: string) =
        let path = Path.Combine(dir, file)

        if File.Exists path then
            let col, rows = readCsv path

            rows
            |> Array.filter (fun c -> c.[col mission] <> "")
            |> Array.map (fun c ->
                (c.[col mission], number c.[col "t_s"], number c.[col "lat"], number c.[col "lon"], number c.[col "rel_alt_m"]))
        else
            [||]

    let planRaw = read "plan_tracks.csv" "mission"
    let flownRaw = read "telemetry.csv" "sortie"
    let reference = if planRaw.Length > 0 then planRaw else flownRaw

    if reference.Length = 0 then
        ([||], [||])
    else
        let lat0 = reference |> Array.averageBy (fun (_, _, la, _, _) -> la)
        let lon0 = reference |> Array.averageBy (fun (_, _, _, lo, _) -> lo)
        let earth = 6371000.0

        let group (rows: (string * float * float * float * float)[]) : (string * Track)[] =
            rows
            |> Array.groupBy (fun (m, _, _, _, _) -> m)
            |> Array.map (fun (m, xs) ->
                (m,
                 xs
                 |> Array.map (fun (_, t, la, lo, z) ->
                     (t,
                      (lo - lon0) * Math.PI / 180.0 * earth * Math.Cos(lat0 * Math.PI / 180.0),
                      (la - lat0) * Math.PI / 180.0 * earth,
                      z))
                 |> Array.sortBy (fun (t, _, _, _) -> t)))
            |> Array.sortBy fst

        (group planRaw, group flownRaw)

/// Where a track is at t, between its points.
let trackAt (track: Track) (t: float) =
    if track.Length = 0 then
        None
    else
        let t0, _, _, _ = track.[0]
        let t1, _, _, _ = track.[track.Length - 1]

        if t < t0 || t > t1 then
            None
        else
            // The first point at or after t, by bisection.
            let mutable lo, hi = 0, track.Length - 1

            while lo < hi do
                let mid = (lo + hi) / 2
                let tm, _, _, _ = track.[mid]

                if tm >= t then hi <- mid else lo <- mid + 1

            let i = lo

            if i = 0 then
                let _, x, y, z = track.[0]
                Some(x, y, z)
            else
                let ta, xa, ya, za = track.[i - 1]
                let tb, xb, yb, zb = track.[i]
                let f = if tb > ta then (t - ta) / (tb - ta) else 0.0
                Some(xa + f * (xb - xa), ya + f * (yb - ya), za + f * (zb - za))

/// The closest two aircraft come while at least one is airborne: (distance,
/// time, one, other, where one is, where the other is). Exact for the tracks as
/// drawn: between their points both move in straight lines, so on each stretch
/// between two points of either track the distance has one minimum.
let closestPair (tracks: (string * Track)[]) =
    let times (tr: Track) = tr |> Array.map (fun (t, _, _, _) -> t)

    let pairClosest (ma: string, ta: Track) (mb: string, tb: Track) =
        let timesA, timesB = times ta, times tb
        let lo = max timesA.[0] timesB.[0]
        let hi = min (Array.last timesA) (Array.last timesB)

        if lo > hi then
            [||]
        else
            let breaks =
                Array.concat [ [| lo; hi |]; timesA; timesB ]
                |> Array.filter (fun t -> t >= lo && t <= hi)
                |> Array.distinct
                |> Array.sort

            let at (t: float) = (trackAt ta t).Value, (trackAt tb t).Value

            let candidate (t: float) =
                let (xa, ya, za), (xb, yb, zb) = at t

                if za > airborneM || zb > airborneM then
                    Some(Math.Sqrt((xa - xb) ** 2.0 + (ya - yb) ** 2.0 + (za - zb) ** 2.0), t, ma, mb, (xa, ya, za), (xb, yb, zb))
                else
                    None

            [|
                for k in 0 .. breaks.Length - 2 do
                    let t1, t2 = breaks.[k], breaks.[k + 1]
                    let (xa1, ya1, za1), (xb1, yb1, zb1) = at t1
                    let (xa2, ya2, za2), (xb2, yb2, zb2) = at t2
                    // The separation at t1 and how it changes to t2.
                    let rx, ry, rz = xa1 - xb1, ya1 - yb1, za1 - zb1
                    let vx, vy, vz = (xa2 - xb2) - rx, (ya2 - yb2) - ry, (za2 - zb2) - rz
                    let vv = vx * vx + vy * vy + vz * vz
                    let s = if vv > 0.0 then max 0.0 (min 1.0 (-(rx * vx + ry * vy + rz * vz) / vv)) else 0.0

                    for t in [ t1; t1 + s * (t2 - t1); t2 ] do
                        match candidate t with
                        | Some c -> c
                        | None -> ()
            |]

    [|
        for a in 0 .. tracks.Length - 1 do
            for b in a + 1 .. tracks.Length - 1 do
                let ma, ta = tracks.[a]
                let mb, tb = tracks.[b]

                if aircraftOf ma <> aircraftOf mb && ta.Length > 0 && tb.Length > 0 then
                    yield! pairClosest (ma, ta) (mb, tb)
    |]
    |> Array.sortBy (fun (d, _, _, _, _, _) -> d)
    |> Array.tryHead

/// After a flight, or with --check on its telemetry.csv: the closest two
/// airborne aircraft came, and how far each mission flew from its planned
/// track when the export wrote plan_tracks.csv.
let checkFlight () =
    let plan, flown = loadTracks ()

    if flown.Length = 0 then
        printfn "No telemetry.csv to check."
    else
        match closestPair flown with
        | Some(d, t, a, b, _, _) -> printfn "Closest approach between airborne aircraft: %.2f m (%s / %s at T0+%.0f s)" d a b t
        | None -> ()

        if plan.Length > 0 then
            let planned = Map.ofArray plan
            printfn "Flown against planned tracks (airborne fixes; outside = airborne before or after the plan's own span):"
            printfn "  %-16s %8s %8s %9s %9s" "mission" "fixes" "outside" "max m" "p95 m"

            for m, tr in flown do
                match planned.TryFind m with
                | Some p when p.Length > 0 ->
                    let pt0, px0, py0, pz0 = p.[0]
                    let pt1, px1, py1, pz1 = p.[p.Length - 1]

                    // Before its plan starts the aircraft should be where the
                    // plan starts, after it ends where it ends.
                    let plannedAt (t: float) =
                        if t < pt0 then (px0, py0, pz0)
                        elif t > pt1 then (px1, py1, pz1)
                        else (trackAt p t).Value

                    let airborne = tr |> Array.filter (fun (_, _, _, z) -> z > airborneM)
                    let outside = airborne |> Array.filter (fun (t, _, _, _) -> t < pt0 || t > pt1) |> Array.length

                    let errors =
                        airborne
                        |> Array.map (fun (t, x, y, z) ->
                            let px, py, pz = plannedAt t
                            Math.Sqrt((x - px) ** 2.0 + (y - py) ** 2.0 + (z - pz) ** 2.0))
                        |> Array.sort

                    if errors.Length > 0 then
                        printfn
                            "  %-16s %8d %8d %9.1f %9.1f"
                            m
                            errors.Length
                            outside
                            errors.[errors.Length - 1]
                            errors.[int (0.95 * float (errors.Length - 1))]
                    else
                        printfn "  %-16s never airborne" m
                | Some _ -> printfn "  %-16s flown, but its planned track is empty" m
                | None -> printfn "  %-16s flown, but not in the plan" m

            let flownNames = flown |> Array.map fst |> Set.ofArray
            let missing = plan |> Array.map fst |> Array.filter (fun m -> not (flownNames.Contains m))

            if missing.Length > 0 then
                printfn "  planned but never flown: %s" (String.Join(", ", missing))

let palette =
    [| "#1f77b4"; "#d62728"; "#2ca02c"; "#9467bd"; "#ff7f0e"; "#17becf"; "#8c564b"; "#e377c2"; "#7f7f7f"; "#bcbd22" |]

/// The colour of aircraft `i` (as "#rrggbb"): the palette first, then for a
/// larger fleet hues spread around the wheel by the golden angle, so no two
/// aircraft share a colour.
let colourHex (i: int) =
    if i < palette.Length then
        palette.[i]
    else
        let hue = (float (i - palette.Length) * 137.508 + 15.0) % 360.0
        let s, v = 0.7, 0.75
        let c = v * s
        let x = c * (1.0 - abs ((hue / 60.0) % 2.0 - 1.0))
        let m = v - c

        let r, g, b =
            match int (hue / 60.0) with
            | 0 -> (c, x, 0.0)
            | 1 -> (x, c, 0.0)
            | 2 -> (0.0, c, x)
            | 3 -> (0.0, x, c)
            | 4 -> (x, 0.0, c)
            | _ -> (c, 0.0, x)

        let byte (f: float) = int (Math.Round((f + m) * 255.0))
        sprintf "#%02x%02x%02x" (byte r) (byte g) (byte b)

/// A round step for axis ticks and the scale bar: 1, 2 or 5 times a power of ten.
let niceStep (span: float) (count: float) =
    let raw = max 1e-9 (span / count)
    let p = Math.Pow(10.0, Math.Floor(Math.Log10 raw))
    [ 1.0; 2.0; 5.0; 10.0 ] |> List.map (fun k -> k * p) |> List.find (fun s -> s >= raw)

/// Draw the plan, and the flight when there is one, as an SVG: the tracks from
/// above (one colour per aircraft; with a flight, the plan is a wide pale band
/// under the thin flown line) with the closest approach circled, and height
/// over time below. Writes flight.svg after a flight, plan.svg from the plan
/// alone.
let drawFlight () =
    let plan, flown = loadTracks ()

    if plan.Length = 0 && flown.Length = 0 then
        printfn "No plan_tracks.csv or telemetry.csv to draw."
    else
        let aircraft =
            Array.append (plan |> Array.map fst) (flown |> Array.map fst)
            |> Array.map aircraftOf
            |> Array.distinct
            // In natural order: PAD-2 before PAD-10.
            |> Array.sortBy (fun a -> Text.RegularExpressions.Regex.Replace(a, "\\d+", fun m -> m.Value.PadLeft(9, '0')))

        let colourOf (hex: string) =
            Colour.FromRgb(Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16))

        let colour (mission: string) =
            colourOf (colourHex (Array.IndexOf(aircraft, aircraftOf mission)))

        let points = Array.append (plan |> Array.collect snd) (flown |> Array.collect snd)
        let xs = points |> Array.map (fun (_, x, _, _) -> x)
        let ys = points |> Array.map (fun (_, _, y, _) -> y)
        let ts = points |> Array.map (fun (t, _, _, _) -> t)
        let zs = points |> Array.map (fun (_, _, _, z) -> z)
        let span = 1.1 * max 1.0 (max (Array.max xs - Array.min xs) (Array.max ys - Array.min ys))
        let cx, cy = (Array.max xs + Array.min xs) / 2.0, (Array.max ys + Array.min ys) / 2.0

        // The first view is from above, north up. A show flown in a vertical
        // plane is a line from above, so then it is from the side, across the
        // plane. Height over time below it.
        let xSpan, ySpan = Array.max xs - Array.min xs, Array.max ys - Array.min ys
        let zSpan = Array.max zs - Array.min zs

        let view =
            if ySpan < 0.1 * xSpan && zSpan > 2.0 * ySpan then "From the south: east to the right, height up"
            elif xSpan < 0.1 * ySpan && zSpan > 2.0 * xSpan then "From the east: north to the right, height up"
            else "From above, north up"

        let fromAbove = view.StartsWith "From above"

        let across (x: float, y: float, z: float) =
            if fromAbove then (x, y)
            elif view.StartsWith "From the south" then (x, z)
            else (y, z)

        let seen = points |> Array.map (fun (_, x, y, z) -> across (x, y, z))
        let hs, vs = Array.map fst seen, Array.map snd seen
        let viewSpan = 1.1 * max 1.0 (max (Array.max hs - Array.min hs) (Array.max vs - Array.min vs))
        let hc, vc = (Array.max hs + Array.min hs) / 2.0, (Array.max vs + Array.min vs) / 2.0
        let width, margin, top = 820.0, 50.0, 70.0
        let side = width - 2.0 * margin
        let px (p: float * float * float) = margin + (fst (across p) - (hc - viewSpan / 2.0)) / viewSpan * side
        let py (p: float * float * float) = top + side - (snd (across p) - (vc - viewSpan / 2.0)) / viewSpan * side
        let chartTop = top + side + 70.0
        let chartHeight = 220.0
        let tMin, tMax = Array.min ts, max (Array.min ts + 1.0) (Array.max ts)
        let zMax = max 1.0 (Array.max zs * 1.1)
        let tx (t: float) = margin + (t - tMin) / (tMax - tMin) * side
        let tz (z: float) = chartTop + chartHeight - max 0.0 z / zMax * chartHeight
        let legendRows = float ((aircraft.Length - 1) / 6 + 1)
        let threeTop = chartTop + chartHeight + 44.0 + 18.0 * legendRows + 30.0
        let threeSize = side * 0.8
        let height = threeTop + 30.0 + threeSize + 20.0

        let page = Page(width, height)
        let g = page.Graphics
        let family (f: FontFamily.StandardFontFamilies) = FontFamily.ResolveFontFamily f
        let font = Font(family FontFamily.StandardFontFamilies.Helvetica, 12.0)
        let bold = Font(family FontFamily.StandardFontFamilies.HelveticaBold, 12.0)
        let titleFont = Font(family FontFamily.StandardFontFamilies.HelveticaBold, 17.0)
        let grey = Colour.FromRgb(85uy, 85uy, 85uy)
        let light = Colour.FromRgb(229uy, 229uy, 229uy)
        let frame = Colour.FromRgb(204uy, 204uy, 204uy)
        let panel = Colour.FromRgb(250uy, 250uy, 250uy)

        let text (x: float) (y: float) (f: Font) (c: Colour) (s: string) =
            g.FillText(x, y, s, f, c, TextBaselines.Baseline)

        let centred (x: float) (y: float) (s: string) =
            text (x - font.MeasureText(s).Width / 2.0) y font grey s

        let rightAligned (x: float) (y: float) (s: string) =
            text (x - font.MeasureText(s).Width) y font grey s

        let line (x1: float) (y1: float) (x2: float) (y2: float) (c: Colour) (w: float) =
            g.StrokePath(GraphicsPath().MoveTo(x1, y1).LineTo(x2, y2), c, w)

        // At most 1500 points per line.
        let thin (track: Track) =
            let k = max 1 (track.Length / 1500)
            track |> Array.indexed |> Array.filter (fun (i, _) -> i % k = 0 || i = track.Length - 1) |> Array.map snd

        let polyline (coords: (float * float)[]) (c: Colour) (w: float) =
            if coords.Length > 1 then
                let path = GraphicsPath().MoveTo(fst coords.[0], snd coords.[0])

                for x, y in Array.skip 1 coords do
                    path.LineTo(x, y) |> ignore

                g.StrokePath(path, c, w, lineCap = LineCaps.Round, lineJoin = LineJoins.Round)

        // With a flight, the plan is a wide pale band and the flight a thin line
        // on it: a flight that keeps to its plan stays inside the band. Dashes
        // would vanish under a flight that matches.
        let planned (c: Colour) = Colour.FromRgba(c.R, c.G, c.B, 0.28)
        let bandW, flownW = 7.0, 1.6

        let planStroke (m: string) =
            if flown.Length > 0 then (planned (colour m), bandW) else (colour m, 1.8)

        let circle (x: float) (y: float) (r: float) =
            GraphicsPath().Arc(x, y, r, 0.0, 2.0 * Math.PI).Close()

        // The run's name: the folder the mavlink folder is in.
        let run = DirectoryInfo(dir)
        let runName = if run.Name = "mavlink" && not (isNull run.Parent) then run.Parent.Name else run.Name

        g.FillRectangle(0.0, 0.0, width, height, Colours.White)
        text margin 28.0 titleFont Colours.Black (if flown.Length > 0 then "Planned and flown tracks" else "Planned tracks")
        text margin 48.0 font grey runName

        // The key, under the clock.
        if flown.Length > 0 then
            let keyY = 50.0
            let flownLabel, planLabel = "flown", "planned"
            let flownX = margin + side - font.MeasureText(flownLabel).Width
            let planX = flownX - 76.0 - font.MeasureText(planLabel).Width

            g.StrokePath(GraphicsPath().MoveTo(planX, keyY - 4.0).LineTo(planX + 20.0, keyY - 4.0), planned grey, bandW, lineCap = LineCaps.Round)
            text (planX + 28.0) keyY font grey planLabel
            line (flownX - 34.0) (keyY - 4.0) (flownX - 8.0) (keyY - 4.0) grey flownW
            text flownX keyY font grey flownLabel

        // The first view, with a scale bar and, from above, north up.
        g.FillRectangle(margin, top, side, side, panel)
        g.StrokeRectangle(margin, top, side, side, frame, 1.0)
        text (margin + 10.0) (top + 18.0) font grey view
        let bar = niceStep viewSpan 5.0
        line (margin + 12.0) (top + side - 14.0) (margin + 12.0 + bar / viewSpan * side) (top + side - 14.0) grey 2.0
        text (margin + 12.0) (top + side - 20.0) font grey (sprintf "%g m" bar)

        if fromAbove then
            let nx, ny = margin + side - 24.0, top + 14.0
            line nx (ny + 22.0) nx ny grey 1.5
            g.FillPath(GraphicsPath().MoveTo(nx, ny - 4.0).LineTo(nx - 5.0, ny + 6.0).LineTo(nx + 5.0, ny + 6.0).Close(), grey)
            text (nx + 7.0) (ny + 10.0) bold grey "N"

        for m, tr in plan do
            let c, w = planStroke m
            polyline (thin tr |> Array.map (fun (_, x, y, z) -> (px (x, y, z), py (x, y, z)))) c w

        for m, tr in flown do
            polyline (thin tr |> Array.map (fun (_, x, y, z) -> (px (x, y, z), py (x, y, z)))) (colour m) flownW

        // Where each aircraft starts (its pad).
        for m, tr in Array.append plan flown do
            let _, x, y, z = tr.[0]
            g.FillPath(circle (px (x, y, z)) (py (x, y, z)) 3.5, colour m)

        // The closest approach: of the flight if there is one, else of the plan.
        match closestPair (if flown.Length > 0 then flown else plan) with
        | Some(d, t, a, b, pa, pb) ->
            line (px pa) (py pa) (px pb) (py pb) Colours.Black 1.5
            g.StrokePath(circle ((px pa + px pb) / 2.0) ((py pa + py pb) / 2.0) 9.0, Colours.Black, 1.5)

            text
                margin
                (top + side + 22.0)
                font
                grey
                (sprintf
                    "Closest %s: %.2f m, %s / %s at T0+%.0f s (circled)"
                    (if flown.Length > 0 then "flown" else "planned")
                    d
                    a
                    b
                    t)
        | None -> ()

        // Height over time.
        text margin (chartTop - 12.0) bold Colours.Black "Height above home (m) over time (s after T0)"
        g.FillRectangle(margin, chartTop, side, chartHeight, panel)
        let tStep = niceStep (tMax - tMin) 8.0

        for t in Math.Ceiling(tMin / tStep) * tStep .. tStep .. tMax do
            line (tx t) chartTop (tx t) (chartTop + chartHeight) light 1.0
            centred (tx t) (chartTop + chartHeight + 16.0) (sprintf "%g" t)

        let zStep = niceStep zMax 4.0

        for z in 0.0 .. zStep .. zMax do
            line margin (tz z) (margin + side) (tz z) light 1.0
            rightAligned (margin - 6.0) (tz z + 4.0) (sprintf "%g" z)

        g.StrokeRectangle(margin, chartTop, side, chartHeight, frame, 1.0)

        for m, tr in plan do
            let c, w = planStroke m
            polyline (thin tr |> Array.map (fun (t, _, _, z) -> (tx t, tz z))) c (if flown.Length > 0 then 0.8 * w else 1.4)

        for m, tr in flown do
            polyline (thin tr |> Array.map (fun (t, _, _, z) -> (tx t, tz z))) (colour m) (0.8 * flownW)

        // Legend: one colour per aircraft.
        aircraft
        |> Array.iteri (fun i a ->
            let x = margin + float (i % 6) * 125.0
            let y = chartTop + chartHeight + 44.0 + float (i / 6) * 18.0
            g.FillRectangle(x, y - 10.0, 12.0, 12.0, colourOf (colourHex i))
            text (x + 17.0) y font Colours.Black a)

        // 3-D view from the south-west, above: the ground as a square, heights
        // exaggerated so the climbs and descents show at any scale. VectSharp's
        // 3-D frame has Y down the screen, so east is X, north Z, up -Y.
        let exaggerate = max 1.0 (0.35 * span / max 1.0 (Array.max zs))
        let at (x: float) (y: float) (z: float) = Point3D(x - cx, -z * exaggerate, y - cy)
        let scene = Scene()
        let h = span / 2.0

        for (ax, ay), (bx, by) in [ ((-h, -h), (h, -h)); ((h, -h), (h, h)); ((h, h), (-h, h)); ((-h, h), (-h, -h)) ] do
            scene.AddElement(Line3DElement(at (cx + ax) (cy + ay) 0.0, at (cx + bx) (cy + by) 0.0, Colour = frame, Thickness = 1.0))

        // North and east from the south-west corner, on the ground.
        let corner = (cx - h, cy - h)
        let arrow = span / 6.0
        let northTip = (fst corner, snd corner + arrow)
        let eastTip = (fst corner + arrow, snd corner)

        for tipX, tipY in [ northTip; eastTip ] do
            scene.AddElement(Line3DElement(at (fst corner) (snd corner) 0.0, at tipX tipY 0.0, Colour = grey, Thickness = 2.0))

        let add3 (m: string, tr: Track) (dashed: bool) =
            let k = max 1 (tr.Length / 150)
            let pts = tr |> Array.indexed |> Array.filter (fun (i, _) -> i % k = 0 || i = tr.Length - 1) |> Array.map snd

            for (_, xa, ya, za), (_, xb, yb, zb) in Array.pairwise pts do
                let l = Line3DElement(at xa ya za, at xb yb zb, Colour = colour m, Thickness = (if dashed then 1.0 else 1.8))

                if dashed then
                    l.LineDash <- LineDash(4.0, 3.0, 0.0)

                scene.AddElement l

        // The flight if there is one, else the plan: the views above compare them.
        if flown.Length > 0 then
            for track in flown do
                add3 track false
        else
            for track in plan do
                add3 track true

        let direction = Vector3D(0.55, 0.6, 0.55).Normalize()
        let position = Point3D(-direction.X * span * 3.0, -direction.Y * span * 3.0, -direction.Z * span * 3.0)

        // Fit the view to everything in it: tall shows and flat fleets alike.
        let extent =
            let unit = OrthographicCamera(position, direction, Size(1.0, 1.0), 1.0)

            [
                for tipX, tipY in [ (cx - h, cy - h); (cx + h, cy - h); (cx + h, cy + h); (cx - h, cy + h) ] -> at tipX tipY 0.0
                for _, tr in Array.append plan flown do
                    for _, x, y, z in tr -> at x y z
            ]
            |> List.map unit.Project
            |> List.collect (fun p -> [ abs p.X; abs p.Y ])
            |> List.fold max 1.0

        let viewSize = 2.1 * extent
        let scale3 = threeSize / viewSize
        let camera = OrthographicCamera(position, direction, Size(viewSize, viewSize), scale3)

        let rendered = VectorRenderer().Render(scene, [||], camera)
        let left3 = margin + (side - rendered.Width) / 2.0
        text margin (threeTop + 12.0) bold Colours.Black (sprintf "3-D view from the south-west (heights x%.0f)" exaggerate)
        g.DrawGraphics(left3, threeTop + 30.0, rendered.Graphics)

        // Label the arrows where the camera puts their tips.
        for (tipX, tipY), label in [ (northTip, "N"); (eastTip, "E") ] do
            let p = camera.Project(at tipX tipY 0.0)
            // Project gives page units around the view's centre.
            text (left3 + p.X - camera.TopLeft.X + 4.0) (threeTop + 30.0 + p.Y - camera.TopLeft.Y + 4.0) bold grey label

        // The picture as an SVG document, then animated: every aircraft moves as
        // a dot in all three views, a cursor sweeps the height chart and a clock
        // runs, the whole flight in `animationS` seconds, looping. A viewer that
        // does not animate shows the first moment.
        let doc =
            SVGContextInterpreter.SaveAsSVG(
                page,
                SVGContextInterpreter.TextOptions.SubsetFonts,
                null,
                SVGContextInterpreter.FilterOption(SVGContextInterpreter.FilterOption.FilterOperations.RasteriseIfNecessary, 1.0, true),
                false
            )

        let svgNs = "http://www.w3.org/2000/svg"
        let root = doc.DocumentElement
        // A size of its own, so an <img> of it (a README on GitHub) shows it at
        // that size, or narrower, rather than at a browser default.
        root.SetAttribute("width", sprintf "%.0f" width)
        root.SetAttribute("height", sprintf "%.0f" height)

        // Tenths of a unit are plenty for a drawing; VectSharp writes every
        // coordinate to 15 digits.
        let decimals = Text.RegularExpressions.Regex("-?\\d+\\.\\d+")

        for node in doc.GetElementsByTagName("path") |> Seq.cast<Xml.XmlElement> do
            let d = node.GetAttribute("d")

            if d <> "" then
                node.SetAttribute("d", decimals.Replace(d, fun m -> Math.Round(Double.Parse(m.Value, invariant), 1).ToString(invariant)))
        let animationS = 20.0
        let steps = 400
        let f1 (v: float) = v.ToString("0.#", invariant)
        let dur = sprintf "%gs" animationS

        let element (name: string) (attributes: (string * string) list) (parent: Xml.XmlNode) =
            let e = doc.CreateElement(name, svgNs)

            for a, v in attributes do
                e.SetAttribute(a, v)

            parent.AppendChild e |> ignore
            e

        let animate (parent: Xml.XmlNode) (attribute: string) (values: string seq) (calcMode: string) =
            element
                "animate"
                [
                    "attributeName", attribute
                    "values", String.Join(";", values)
                    "dur", dur
                    "calcMode", calcMode
                    "repeatCount", "indefinite"
                ]
                parent
            |> ignore

        let times = [| for k in 0..steps -> tMin + (tMax - tMin) * float k / float steps |]

        // Each aircraft's position over the whole run: in one of its sorties
        // (flown if there was a flight, else planned), else parked where its
        // last sortie ended or its first begins.
        let sortiesOf (a: string) =
            (if flown.Length > 0 then flown else plan) |> Array.filter (fun (m, _) -> aircraftOf m = a) |> Array.map snd

        let positionOf (tracks: Track[]) (t: float) =
            match tracks |> Array.tryPick (fun tr -> trackAt tr t) with
            | Some p -> p
            | None ->
                let ended = tracks |> Array.filter (fun tr -> let e, _, _, _ = tr.[tr.Length - 1] in e < t)

                let _, x, y, z =
                    if ended.Length > 0 then
                        ended |> Array.maxBy (fun tr -> let e, _, _, _ = tr.[tr.Length - 1] in e) |> Array.last
                    else
                        (tracks |> Array.minBy (fun tr -> let s, _, _, _ = tr.[0] in s)).[0]

                (x, y, z)

        // A fixed-wing's missions take off with MAV_CMD_NAV_VTOL_TAKEOFF (84).
        let isPlane (a: string) =
            Array.append plan flown
            |> Array.map fst
            |> Array.distinct
            |> Array.filter (fun m -> aircraftOf m = a)
            |> Array.exists (fun m ->
                let file = Path.Combine(dir, m + ".waypoints")

                File.Exists file
                && File.ReadAllLines file
                   |> Array.exists (fun l ->
                       let c = l.Split('\t')
                       c.Length > 3 && c.[3] = "84"))

        let animateTransform (parent: Xml.XmlNode) (kind: string) (values: string seq) =
            element
                "animateTransform"
                [
                    "attributeName", "transform"
                    "type", kind
                    "values", String.Join(";", values)
                    "dur", dur
                    "calcMode", "linear"
                    "repeatCount", "indefinite"
                ]
                parent
            |> ignore

        // The direction of motion on the page at each step, in degrees and
        // unwrapped (no spin at ±180); held while the aircraft stands still.
        let headings (xs: float[]) (ys: float[]) =
            let n = xs.Length

            let moving =
                Array.init n (fun k ->
                    let a, b = (if k + 1 < n then (k, k + 1) else (max 0 (k - 1), k))
                    let dx, dy = xs.[b] - xs.[a], ys.[b] - ys.[a]

                    if dx * dx + dy * dy > 0.04 then
                        Some(Math.Atan2(dy, dx) * 180.0 / Math.PI)
                    else
                        None)

            let first = moving |> Array.tryPick id |> Option.defaultValue 0.0
            let h = Array.create n first
            let mutable last = first

            for k in 0 .. n - 1 do
                match moving.[k] with
                | Some d -> last <- d
                | None -> ()

                h.[k] <- last

            for k in 1 .. n - 1 do
                while h.[k] - h.[k - 1] > 180.0 do
                    h.[k] <- h.[k] - 360.0

                while h.[k] - h.[k - 1] < -180.0 do
                    h.[k] <- h.[k] + 360.0

            h

        // A fixed-wing seen from above, nose to the right (+x).
        let planeShape =
            "M 8 0 L 2.5 -1.4 L 0.5 -7.5 L -1.5 -7.5 L -1 -1.4 L -5.5 -1.4 L -7 -4.5 L -8.5 -4.5 L -7.5 0 L -8.5 4.5 L -7 4.5 L -5.5 1.4 L -1 1.4 L -1.5 7.5 L 0.5 7.5 L 2.5 1.4 Z"

        // Each aircraft moves as its own icon: a copter as four rotors on an
        // X, a fixed-wing as a plane turned the way it flies; faint on the
        // ground.
        let dot (plane: bool) (fill: string) (xs: float[]) (ys: float[]) (zs: float[]) =
            let g = element "g" [ "transform", sprintf "translate(%s %s)" (f1 xs.[0]) (f1 ys.[0]) ] root
            animateTransform g "translate" (Array.map2 (fun x y -> f1 x + " " + f1 y) xs ys)
            animate g "opacity" (zs |> Array.map (fun z -> if z > airborneM then "1" else "0.35")) "linear"

            // The shapes are drawn 1.5 times their unit size.
            if plane then
                let turns = headings xs ys
                let body = element "g" [ "transform", sprintf "rotate(%s)" (f1 turns.[0]) ] g
                animateTransform body "rotate" (turns |> Array.map f1)

                element
                    "path"
                    [
                        "d", planeShape
                        "transform", "scale(1.5)"
                        "fill", fill
                        "stroke", "white"
                        "stroke-width", "0.8"
                        "stroke-linejoin", "round"
                    ]
                    body
                |> ignore
            else
                let quad = element "g" [ "transform", "scale(1.5)" ] g

                for w, c in [ ("3.5", "white"); ("1.8", fill) ] do
                    element "path" [ "d", "M -5 -5 L 5 5 M -5 5 L 5 -5"; "stroke", c; "stroke-width", w; "stroke-linecap", "round"; "fill", "none" ] quad
                    |> ignore

                for x, y in [ (-5, -5); (5, 5); (-5, 5); (5, -5) ] do
                    element "circle" [ "cx", string x; "cy", string y; "r", "2.6"; "fill", fill; "stroke", "white"; "stroke-width", "0.8" ] quad
                    |> ignore

        for i, a in Array.indexed aircraft do
            let tracks = sortiesOf a

            if tracks.Length > 0 then
                let fill = colourHex i
                let dot = dot (isPlane a)
                let where = times |> Array.map (positionOf tracks)
                let heights = where |> Array.map (fun (_, _, z) -> z)
                dot fill (where |> Array.map px) (where |> Array.map py) heights
                dot fill (times |> Array.map tx) (heights |> Array.map tz) heights

                let onScreen =
                    where
                    |> Array.map (fun (x, y, z) ->
                        let p = camera.Project(at x y z)
                        (left3 + p.X - camera.TopLeft.X, threeTop + 30.0 + p.Y - camera.TopLeft.Y))

                dot fill (onScreen |> Array.map fst) (onScreen |> Array.map snd) heights

        // The cursor on the height chart.
        let cursor =
            element
                "line"
                [ "x1", f1 (tx tMin); "x2", f1 (tx tMin); "y1", f1 chartTop; "y2", f1 (chartTop + chartHeight); "stroke", "black"; "stroke-width", "1" ]
                root

        animate cursor "x1" [ f1 (tx tMin); f1 (tx tMax) ] "linear"
        animate cursor "x2" [ f1 (tx tMin); f1 (tx tMax) ] "linear"

        // The clock: one label per step of a twentieth of the flight.
        let ticks = 20

        for k in 0 .. ticks - 1 do
            let label =
                element
                    "text"
                    [
                        "x", f1 (margin + side)
                        "y", "28"
                        "text-anchor", "end"
                        "font-family", "Helvetica, Arial, sans-serif"
                        "font-size", "17"
                        "font-weight", "bold"
                        "visibility", (if k = 0 then "visible" else "hidden")
                    ]
                    root

            label.InnerText <- sprintf "T0+%.0f s" (tMin + (tMax - tMin) * float k / float ticks)
            let a, b = float k / float ticks, float (k + 1) / float ticks

            let values, keyTimes =
                if k = 0 then ("visible;hidden", sprintf "0;%s" (b.ToString(invariant)))
                else ("hidden;visible;hidden", sprintf "0;%s;%s" (a.ToString(invariant)) (b.ToString(invariant)))

            element
                "animate"
                [
                    "attributeName", "visibility"
                    "values", values
                    "keyTimes", keyTimes
                    "dur", dur
                    "calcMode", "discrete"
                    "repeatCount", "indefinite"
                ]
                label
            |> ignore

        let file = if flown.Length > 0 then "flight.svg" else "plan.svg"
        SVGContextInterpreter.WriteSVGXML(doc, Path.Combine(dir, file))
        printfn "Drawn: %s (animated, %g s loop)" (Path.Combine(dir, file)) animationS

if fsi.CommandLineArgs |> Array.contains "--check" then
    checkFlight ()
    drawFlight ()
    exit 0

if fsi.CommandLineArgs |> Array.contains "--draw" then
    drawFlight ()
    exit 0

// =============================================================================
// DRY RUN: every message, no network
// =============================================================================

let files =
    drones
    |> List.map (fun (name, connection, launchS) ->
        let home, vehicleType, items = loadPlan name

        {
            Name = name
            Connection = connection
            LaunchS = launchS
            Home = home
            VehicleType = vehicleType
            Mission = missionOf home items
            Params = loadParams name
        })

/// One vehicle per connection, its sorties in start order.
let vehicles =
    files
    |> List.groupBy (fun s -> s.Connection)
    |> List.map (fun (connection, sorties) -> (connection, sorties |> List.sortBy (fun s -> s.LaunchS)))

/// How far (m) a vehicle may stand from a sortie's home before arming: at
/// most homeToleranceM, and under half the distance between two vehicles'
/// homes, so a vehicle on another's pad never passes for one on its own. Homes
/// closer than 0.1 m are one pad used in turn.
let homeTolerance =
    let homes =
        vehicles |> List.collect (fun (connection, sorties) -> sorties |> List.map (fun s -> (connection, s.Home)))

    [
        for ca, a in homes do
            for cb, b in homes do
                if ca < cb then
                    let d = distanceM a.[0] a.[1] b.[0] b.[1]

                    if d > 0.1 then
                        d / 2.0
    ]
    |> List.fold min homeToleranceM

if dryRun then
    let writer = MAVLink.MavlinkParse(false)
    let packet msgid (payload: obj) = writer.GenerateMAVLinkPacket20(msgid, payload, false, gcsSystem, gcsComponent, 0)
    let hex (b: byte[]) = b |> Array.map (sprintf "%02x") |> String.concat " "

    for s in files do
        printfn "== %s (%s) via %s at T0+%.0f s: home %.7f, %.7f (%.1f m MSL)" s.Name (kindName s.VehicleType) s.Connection s.LaunchS s.Home.[0] s.Home.[1] s.Home.[2]

        let paramPackets =
            s.Params
            |> List.map (fun (n, v) ->
                packet MsgId.PARAM_SET (MAVLink.mavlink_param_set_t(float32 v, 1uy, 1uy, paramId n, 9uy)))

        printfn "  %d PARAM_SET (each read back with PARAM_REQUEST_READ): %s" s.Params.Length (s.Params |> List.map (fun (n, v) -> sprintf "%s=%g" n v) |> String.concat " ")
        printfn "  first PARAM_SET packet: %s" (hex paramPackets.Head)
        printfn "  MISSION_COUNT %d, then MISSION_ITEM_INT on request:" s.Mission.Length

        for k, i in List.indexed s.Mission do
            let bytes = packet MsgId.MISSION_ITEM_INT (missionItemInt 1uy k i)
            printfn "    seq %2d cmd %3d frame %d p1 %8.3f p2 %6.3f  %11.7f %11.7f %7.2f  (%d bytes)" k i.Command (intFrame i.Frame) i.P.[0] i.P.[1] i.P.[4] i.P.[5] i.P.[6] bytes.Length

    for connection, sorties in vehicles do
        let s = sorties.Head

        printfn
            "SITL for %s: sim_vehicle.py -v %s -I <instance> --custom-location=%.7f,%.7f,%.1f,0"
            connection
            (if isPlane s.VehicleType then "ArduPlane -f quadplane" else "ArduCopter")
            s.Home.[0]
            s.Home.[1]
            s.Home.[2]

    printfn "Preflight: vehicle kind, parameters and mission read back equal, vehicle within %.2f m of home; then arm and MISSION_START" homeTolerance
    printfn "Each later sortie: uploaded and read back once the vehicle is disarmed; at launch, within %.2f m of its home with a fix" homeTolerance
    printfn "Dry run: nothing sent."
    exit 0

// =============================================================================
// PREFLIGHT: refuse unless every vehicle matches the files
// =============================================================================

let links =
    vehicles
    |> List.map (fun (connection, sorties) -> Link(sorties.Head.Name, connection))

// Our heartbeat, once a second, to every vehicle
let heartbeatTimer =
    new Timer((fun _ ->
        for l in links do
            try
                l.Send(MsgId.HEARTBEAT, heartbeat ())
            with ex ->
                eprintfn "%s: heartbeat not sent: %s" l.Name ex.Message), null, 0, max 20 (int (1000.0 / speedup)))

/// The first sortie of a vehicle is checked here; a later sortie is checked
/// when it is uploaded, before it starts. Parameters are set once per vehicle,
/// so all its sorties must agree on them.
let preflight (link: Link) (sorties: Sortie list) =
    let s = sorties.Head

    let conflicts =
        sorties
        |> List.filter (fun o -> o.Params <> s.Params || o.VehicleType <> s.VehicleType)
        |> List.map (fun o -> sprintf "%s: its parameters or vehicle kind differ from %s's" o.Name s.Name)

    printfn "%s: waiting for heartbeat..." s.Name

    match link.WaitHeartbeat 30000 with
    | None -> conflicts @ [ sprintf "%s: no heartbeat" s.Name ]
    | Some mavType when not ((if isPlane s.VehicleType then planeTypes else copterTypes).Contains mavType) ->
        conflicts @ [ sprintf "%s: the vehicle reports MAV_TYPE %d; the mission is for %s" s.Name mavType (kindName s.VehicleType) ]
    | Some _ ->
        try
            printfn "%s: setting %d parameters..." s.Name s.Params.Length

            let setProblems =
                s.Params
                |> List.choose (fun (n, v) ->
                    match setParam link n v with
                    | Ok() -> None
                    | Error e -> Some e)

            let paramProblems =
                s.Params
                |> List.choose (fun (n, v) ->
                    match readParam link n with
                    | Ok actual when abs (actual - v) <= 1e-3 * max 1.0 (abs v) -> None
                    | Ok actual -> Some(sprintf "%s: %s = %g, the files %g" s.Name n actual v)
                    | Error _ when setProblems |> List.exists (fun e -> e.Contains(" " + n + " ")) -> None
                    | Error e -> Some e)

            printfn "%s: uploading %d mission items..." s.Name s.Mission.Length
            uploadMission link s.Mission
            let missionProblems = downloadMission link |> missionDifferences s.Mission |> List.map (fun p -> s.Name + ": " + p)

            // Stream position, mission progress and heartbeats
            command link cmdSetMessageInterval [ 33.0; 500000.0 ] // GLOBAL_POSITION_INT at 2 Hz
            command link cmdSetMessageInterval [ 42.0; 1000000.0 ] // MISSION_CURRENT at 1 Hz

            // Before its GPS has a fix a vehicle reports 0, 0: wait for a real
            // position.
            let hasFix (d: obj) =
                let p = unbox<MAVLink.mavlink_global_position_int_t> d
                p.lat <> 0 || p.lon <> 0

            let homeProblems =
                match link.Receive(MsgId.GLOBAL_POSITION_INT, hasFix, 60000) with
                | None -> [ sprintf "%s: no position fix within a minute" s.Name ]
                | Some d ->
                    let p = unbox<MAVLink.mavlink_global_position_int_t> d
                    let off = distanceM (float p.lat / 1e7) (float p.lon / 1e7) s.Home.[0] s.Home.[1]

                    if off > homeTolerance then
                        [ sprintf "%s: stands %.2f m from its declared home (its own slot), at most %.2f m" s.Name off homeTolerance ]
                    else
                        []

            conflicts @ setProblems @ paramProblems @ missionProblems @ homeProblems
        with ex ->
            conflicts @ [ sprintf "%s: %s" s.Name ex.Message ]

let problems =
    List.zip links vehicles |> List.collect (fun (link, (_, sorties)) -> preflight link sorties)

if not problems.IsEmpty then
    eprintfn "REFUSING TO START: the vehicles do not match what the evidence pack checked:"

    for p in problems do
        eprintfn "  %s" p

    exit 1

printfn "Every vehicle carries exactly the checked mission and parameters."

// =============================================================================
// FLY
// =============================================================================

let kinds = vehicles |> List.map (fun (_, sorties) -> sorties.Head.VehicleType) |> Array.ofList
let linksArr = Array.ofList links
let aborted = ref false

Console.CancelKeyPress.Add(fun e ->
    e.Cancel <- true

    if not aborted.Value then
        aborted.Value <- true
        eprintfn "ABORT: RTL for every vehicle"

        for i, l in Array.indexed linksArr do
            match tryCommand l cmdSetMode [ 1.0; rtlMode kinds.[i] ] with
            | Ok() -> ()
            | Error e -> eprintfn "%s" e)

let t0 = ref DateTime.UtcNow
let elapsed () = (DateTime.UtcNow - t0.Value).TotalSeconds * speedup

/// When each vehicle last started a sortie (the launcher's clock).
let startedAt = Array.create linksArr.Length DateTime.MinValue

/// Armed according to the vehicle's own latest heartbeat (base_mode bit 7).
/// A heartbeat from before the last start, or none for 3 s, counts as armed:
/// a sortie is never started on top of one still flying.
let armed (i: int) =
    match linksArr.[i].Latest MsgId.HEARTBEAT with
    | Some(d, at) when at > startedAt.[i].AddSeconds 3.0 && (DateTime.UtcNow - at).TotalSeconds < 3.0 ->
        (unbox<MAVLink.mavlink_heartbeat_t> d).base_mode &&& 128uy <> 0uy
    | _ -> true

/// Set the arming mode and arm, retrying while pre-arm checks refuse. Never
/// after an abort: that would take a vehicle out of the RTL just commanded.
let arm (i: int) (waitS: float) =
    let until = DateTime.UtcNow.AddSeconds waitS

    let rec go () =
        if aborted.Value then
            Error(sprintf "%s: aborted" linksArr.[i].Name)
        else
            match
                tryCommand linksArr.[i] cmdSetMode [ 1.0; armMode kinds.[i] ]
                |> Result.bind (fun () -> tryCommand linksArr.[i] cmdArm [ 1.0 ])
            with
            | Ok() -> Ok()
            | Error _ when DateTime.UtcNow < until ->
                Thread.Sleep 2000
                go ()
            | Error e -> Error e

    go ()

let disarm (i: int) = tryCommand linksArr.[i] cmdArm [ 0.0 ] |> ignore

/// Whether vehicle `i` stands at the sortie's home now, with a fix. A later
/// sortie takes off where the vehicle stands: one that landed elsewhere (a
/// failsafe, a pilot's LAND) would fly a path nobody checked.
let atHome (i: int) (s: Sortie) =
    match linksArr.[i].Latest MsgId.GLOBAL_POSITION_INT with
    | Some(d, at) when (DateTime.UtcNow - at).TotalSeconds < 3.0 ->
        let p = unbox<MAVLink.mavlink_global_position_int_t> d

        if p.lat = 0 && p.lon = 0 then
            Error "no position fix"
        else
            let off = distanceM (float p.lat / 1e7) (float p.lon / 1e7) s.Home.[0] s.Home.[1]

            if off > homeTolerance then
                Error(sprintf "it stands %.2f m from the sortie's home, at most %.2f m" off homeTolerance)
            else
                Ok()
    | _ -> Error "no recent position"

/// Start the mission on board, and whether the vehicle accepted it.
let missionStart (i: int) =
    if aborted.Value then
        Error(sprintf "%s: aborted" linksArr.[i].Name)
    else
        startedAt.[i] <- DateTime.UtcNow
        // Sent once: a repeat could restart a mission already under way.
        tryCommandTries 1 linksArr.[i] cmdMissionStart [ 0.0; 0.0 ]

// Sorties still to fly, per vehicle, and the one each is flying.
let queues = vehicles |> List.map (fun (_, sorties) -> ref sorties) |> Array.ofList
let flying = Array.create linksArr.Length ""

// Whether the queue's next sortie is on board and verified: every first sortie
// was, in preflight.
let ready = Array.create linksArr.Length true

// When each vehicle's telemetry was last logged.
let logged = Array.create linksArr.Length DateTime.MinValue

// Everything due at T0 arms first and then starts at the same moment: a
// synchronised show's timing depends on it. The first sortie of every vehicle
// is already on board and verified.
let atT0 =
    [ 0 .. linksArr.Length - 1 ]
    |> List.filter (fun i ->
        match queues.[i].Value with
        | s :: _ -> s.LaunchS <= 0.0
        | [] -> false)

let notStarting (reason: string) =
    eprintfn "NOT STARTING: %s" reason

    for j in atT0 do
        disarm j

    exit 1

for i in atT0 do
    match arm i armWaitS with
    | Ok() -> ()
    | Error e -> notStarting e

// A vehicle armed early may have disarmed itself (DISARM_DELAY) while a later
// one waited on its pre-arm checks: arm them all again, back to back (arming
// an armed vehicle is accepted at once), then start them together.
for i in atT0 do
    match arm i 0.0 with
    | Ok() -> ()
    | Error e -> notStarting e

t0.Value <- DateTime.UtcNow

// Every start goes out back to back, then the acknowledgements are collected:
// a slow acknowledgement must not delay the vehicles after it.
let t0Sent =
    [
        for i in atT0 do
            flying.[i] <- queues.[i].Value.Head.Name
            ready.[i] <- false
            queues.[i].Value <- queues.[i].Value.Tail

            if aborted.Value then
                (i, Error "aborted")
            else
                startedAt.[i] <- DateTime.UtcNow

                try
                    // Sent once: a repeat could restart a mission already under way.
                    linksArr.[i].Send(MsgId.COMMAND_LONG, commandLong linksArr.[i].Target cmdMissionStart [ 0.0; 0.0 ])
                    (i, Ok(elapsed ()))
                with ex ->
                    (i, Error ex.Message)
    ]

for i, sent in t0Sent do
    let acknowledged =
        sent
        |> Result.bind (fun _ ->
            let isStart (d: obj) =
                int (unbox<MAVLink.mavlink_command_ack_t> d).command = cmdMissionStart

            match linksArr.[i].Receive(MsgId.COMMAND_ACK, isStart, 1500) with
            | Some d when (unbox<MAVLink.mavlink_command_ack_t> d).result = 0uy -> Ok()
            | Some d -> Error(sprintf "MAV_RESULT %d" (unbox<MAVLink.mavlink_command_ack_t> d).result)
            | None -> Error "no acknowledgement")

    match acknowledged with
    | Ok() -> ()
    | Error e -> eprintfn "%s: start not confirmed, watch the vehicle: %s" flying.[i] e

let t0Spread =
    t0Sent
    |> List.choose (fun (_, sent) ->
        match sent with
        | Ok t -> Some t
        | Error _ -> None)
    |> List.fold max 0.0

if t0Spread > lateToleranceS then
    eprintfn "WARNING: the T0 starts took %.1f s to send; the plan allows %.1f s" t0Spread lateToleranceS

printfn "T0: %d mission(s) started, %d later sortie(s) scheduled. Ctrl+C = RTL for all." atT0.Length (queues |> Array.sumBy (fun q -> q.Value.Length))

let log = new StreamWriter(Path.Combine(dir, "telemetry.csv"))
log.WriteLine "t_s,vehicle,sortie,lat,lon,rel_alt_m,mission_seq,armed,vehicle_ms"

let mutable finished = false

while not finished do
    for i in 0 .. linksArr.Length - 1 do
        let l = linksArr.[i]

        // A later sortie: uploaded and verified as soon as the vehicle is back
        // on the ground and disarmed, then armed and started at its time.
        match queues.[i].Value with
        | _ when aborted.Value -> ()
        | s :: rest when not ready.[i] && not (armed i) ->
            printfn "%s: uploading sortie %s (%d items)..." l.Name s.Name s.Mission.Length

            let differences =
                try
                    uploadMission l s.Mission
                    downloadMission l |> missionDifferences s.Mission
                with ex ->
                    [ ex.Message ]

            match differences with
            | [] -> ready.[i] <- true
            | differences ->
                eprintfn "%s: sortie %s NOT flown, the vehicle does not carry the checked mission:" l.Name s.Name

                for d in differences do
                    eprintfn "  %s" d

                queues.[i].Value <- rest
        | s :: rest when ready.[i] && elapsed () >= s.LaunchS ->
            match atHome i s with
            | Error e -> eprintfn "%s: sortie %s NOT flown: %s" l.Name s.Name e
            | Ok() when elapsed () - s.LaunchS > lateToleranceS ->
                eprintfn "%s: sortie %s NOT flown, %.1f s late (planned T0+%.0f s); the plan was checked for its planned time" l.Name s.Name (elapsed () - s.LaunchS) s.LaunchS
            | Ok() ->
                // Arming can take retries: the lateness is checked again just
                // before the start, since the plan was checked for its planned time.
                match arm i 3.0 with
                | Ok() when elapsed () - s.LaunchS > lateToleranceS ->
                    disarm i
                    eprintfn "%s: sortie %s NOT flown, %.1f s late once armed (planned T0+%.0f s)" l.Name s.Name (elapsed () - s.LaunchS) s.LaunchS
                | Ok() ->
                    match missionStart i with
                    | Ok() ->
                        flying.[i] <- s.Name
                        printfn "%s: sortie %s started at T0+%.1f s (planned %.0f s)" l.Name s.Name (elapsed ()) s.LaunchS
                    | Error e ->
                        // No disarm: with a lost acknowledgement the vehicle
                        // may be taking off. One that did not start disarms
                        // itself (DISARM_DELAY).
                        flying.[i] <- s.Name
                        eprintfn "%s: sortie %s start not confirmed, watch the vehicle: %s" l.Name s.Name e
                | Error e -> eprintfn "%s: sortie %s NOT flown: %s" l.Name s.Name e

            ready.[i] <- false
            queues.[i].Value <- rest
        | _ -> ()

        l.Drain()

        match l.Latest MsgId.GLOBAL_POSITION_INT with
        | Some(d, at) when (DateTime.UtcNow - at).TotalSeconds < 2.0 && (at - logged.[i]).TotalSeconds * speedup >= 0.9 ->
            logged.[i] <- at
            let p = unbox<MAVLink.mavlink_global_position_int_t> d

            let item =
                l.Latest MsgId.MISSION_CURRENT
                |> Option.map (fun (c, _) -> int (unbox<MAVLink.mavlink_mission_current_t> c).seq)
                |> Option.defaultValue -1

            let isArmed = armed i
            let t = (at - t0.Value).TotalSeconds * speedup
            let lat, lon, alt = float p.lat / 1e7, float p.lon / 1e7, float p.relative_alt / 1000.0
            printfn "%s: T0+%.0f item %d  %.7f %.7f  %.1f m%s" l.Name t item lat lon alt (if isArmed then "" else "  (disarmed)")

            log.WriteLine(
                String.Format(
                    Globalization.CultureInfo.InvariantCulture,
                    "{0:0.0},{1},{2},{3:0.0000000},{4:0.0000000},{5:0.00},{6},{7},{8}",
                    t,
                    l.Name,
                    flying.[i],
                    lat,
                    lon,
                    alt,
                    item,
                    (if isArmed then 1 else 0),
                    p.time_boot_ms
                )
            )
        | _ -> ()

    log.Flush()

    // After an abort nothing more starts; the launcher waits for everyone to land.
    if aborted.Value then
        for q in queues do
            q.Value <- []

    finished <-
        (queues |> Array.forall (fun q -> q.Value.IsEmpty)
            && [ 0 .. linksArr.Length - 1 ] |> List.forall (fun i -> not (armed i)))

    if not finished then
        Thread.Sleep(max 10 (int (200.0 / speedup)))

log.Dispose()
printfn "T0+%.0f s: every sortie flown or refused; every vehicle is down and disarmed. Telemetry: telemetry.csv" (elapsed ())
checkFlight ()
drawFlight ()
"""

    /// The script for these missions: their names, connections and start times
    /// are its configuration table; everything else it reads from the files.
    let generate (missions: ScheduledMission list) : string =
        let drones =
            missions
            |> List.map (fun s ->
                sprintf
                    "        (\"%s\", \"%s\", %s)"
                    s.Mission.Drone.Name
                    s.Mission.Drone.ConnectionString
                    // Always with a decimal point, so the script's tuple is a float.
                    (s.LaunchS.ToString("0.0###", Globalization.CultureInfo.InvariantCulture)))
            |> String.concat "\n"

        template
            .TrimStart('\n')
            .Replace("{{DRONES}}", drones)
            // The lateness every booking of a shared place was widened by.
            .Replace("{{LATE}}", Flight.startSlackS.ToString("0.0###", Globalization.CultureInfo.InvariantCulture))

    let writeFile (path: string) (missions: ScheduledMission list) =
        File.WriteAllText(path, generate missions)
        printfn "Wrote MAVLink F# launcher to: %s" path
