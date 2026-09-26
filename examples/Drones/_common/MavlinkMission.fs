/// MAVLink missions for ArduPilot vehicles: the item types, the file formats a
/// ground station loads (QGroundControl .plan, MAVLink .waypoints, ArduPilot
/// .parm) and the generated F# launcher that uploads, verifies and flies them.
///
/// Shared by the drone examples: SwarmChoreography exports a synchronised show,
/// FireAirBridge exports each drone's sorties on the air-bridge lanes. What a
/// mission contains is the example's business; this module only knows how to
/// write it down and fly it.
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
    | DoJump // MAV_CMD_DO_JUMP (177)
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
    let writeAll (baseDir: string) (missions: DroneMission list) =
        Directory.CreateDirectory(baseDir) |> ignore

        for mission in missions do
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
    let writeAll (baseDir: string) (missions: DroneMission list) =
        Directory.CreateDirectory(baseDir) |> ignore

        for mission in missions do
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
// Flies the exported missions on ArduPilot Copter vehicles over MAVLink 2.
// TRY IT AGAINST ARDUPILOT SITL BEFORE ANY REAL AIRCRAFT.
//
//   dotnet fsi mavlink_show.fsx --dry-run   loads the files, builds every message,
//                                           prints what it would send; no network
//   dotnet fsi mavlink_show.fsx             connects and flies
//
// Per vehicle it connects, sets every parameter from <Name>.parm and reads each
// one back, uploads <Name>_mission.plan and reads the whole mission back, and
// checks the vehicle stands on its declared home (its own slot). It REFUSES to
// start unless every vehicle carries exactly the mission and parameters the
// evidence pack checked. Then it arms and starts every mission due at T0 at the
// same moment, and streams telemetry. A vehicle with several missions (sorties)
// gets each later one uploaded, verified and started at its planned time, once
// it has landed and disarmed. Ctrl+C sends every drone RTL (the modelled abort).
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

/// How late (s) a later sortie may start. Its slots were booked against the
/// other aircraft for its planned time; later than this it is not flown.
let lateToleranceS = 5.0

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
    |> List.map (fun (name, connection, launchS) ->
        let home, items = loadPlan name
        (name, connection, launchS, home, missionOf home items, loadParams name))

/// One vehicle per connection, its sorties in start order.
let vehicles =
    files
    |> List.groupBy (fun (_, connection, _, _, _, _) -> connection)
    |> List.map (fun (connection, sorties) ->
        (connection, sorties |> List.sortBy (fun (_, _, launchS, _, _, _) -> launchS)))

if dryRun then
    let writer = MAVLink.MavlinkParse(false)
    let packet msgid (payload: obj) = writer.GenerateMAVLinkPacket20(msgid, payload, false, gcsSystem, gcsComponent, 0)
    let hex (b: byte[]) = b |> Array.map (sprintf "%02x") |> String.concat " "

    for name, connection, launchS, home, mission, parms in files do
        printfn "== %s via %s at T0+%.0f s: home %.7f, %.7f (%.1f m MSL)" name connection launchS home.[0] home.[1] home.[2]

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

let links =
    vehicles
    |> List.map (fun (connection, sorties) ->
        let name, _, _, _, _, _ = sorties.Head
        Link(name, connection))

// Our heartbeat, once a second, to every vehicle
let heartbeatTimer =
    new Timer((fun _ -> for l in links do l.Send(MsgId.HEARTBEAT, heartbeat ())), null, 0, 1000)

// The first sortie of every vehicle is checked here; a later sortie is checked
// when it is uploaded, before it starts. Parameters are set once per vehicle,
// so all its sorties must agree on them.
let problems =
    List.zip links vehicles
    |> List.collect (fun (link, (_, sorties)) ->
        let name, _, _, home, mission, parms = sorties.Head

        let paramConflicts =
            sorties
            |> List.filter (fun (_, _, _, _, _, p) -> p <> parms)
            |> List.map (fun (n, _, _, _, _, _) -> sprintf "%s: its parameters differ from %s's" n name)

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

            paramConflicts @ paramProblems @ missionProblems @ homeProblems)

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

let start (l: Link) =
    command l cmdSetMode [ 1.0; modeGuided ]
    command l cmdArm [ 1.0 ]
    l.Send(MsgId.COMMAND_LONG, commandLong l.Target cmdMissionStart [ 0.0; 0.0 ])

/// Armed according to the vehicle's own heartbeat (base_mode bit 7). Unknown
/// counts as armed: a sortie is never started on top of one still flying.
let armed (l: Link) =
    match l.Receive(MsgId.HEARTBEAT, (fun d -> (unbox<MAVLink.mavlink_heartbeat_t> d).``type`` <> 6uy), 1500) with
    | Some d -> (unbox<MAVLink.mavlink_heartbeat_t> d).base_mode &&& 128uy <> 0uy
    | None -> true

// Sorties still to fly, per vehicle.
let queues = vehicles |> List.map (fun (_, sorties) -> ref sorties)
let t0 = DateTime.UtcNow
let elapsed () = (DateTime.UtcNow - t0).TotalSeconds

// Everything due at T0 arms first and then starts at the same moment: a
// synchronised show's timing depends on it. The first sortie of every vehicle
// is already on board and verified.
let atT0 =
    List.zip links queues
    |> List.filter (fun (_, q) ->
        match q.Value with
        | (_, _, launchS, _, _, _) :: _ -> launchS <= 0.0
        | [] -> false)

for l, _ in atT0 do
    command l cmdSetMode [ 1.0; modeGuided ]
    command l cmdArm [ 1.0 ]

for l, q in atT0 do
    l.Send(MsgId.COMMAND_LONG, commandLong l.Target cmdMissionStart [ 0.0; 0.0 ])
    q.Value <- q.Value.Tail

printfn "T0: %d mission(s) started, %d later sortie(s) scheduled. Ctrl+C = RTL for all." atT0.Length (queues |> List.sumBy (fun q -> q.Value.Length))

while true do
    for l, q in List.zip links queues do
        // A later sortie: due, and the vehicle back on the ground and disarmed.
        match q.Value with
        | (name, _, launchS, _, mission, _) :: rest when elapsed () >= launchS && not (armed l) ->
            printfn "%s: uploading sortie %s (%d items)..." l.Name name mission.Length
            uploadMission l mission

            match downloadMission l |> missionDifferences mission with
            | [] when elapsed () - launchS > lateToleranceS ->
                eprintfn "%s: sortie %s NOT flown, %.0f s late (planned T0+%.0f s); its slots were booked for then" l.Name name (elapsed () - launchS) launchS
            | [] ->
                start l
                printfn "%s: sortie %s started at T0+%.0f s (planned %.0f s)" l.Name name (elapsed ()) launchS
            | differences ->
                eprintfn "%s: sortie %s NOT flown, the vehicle does not carry the checked mission:" l.Name name

                for d in differences do
                    eprintfn "  %s" d

            q.Value <- rest
        | _ -> ()

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

        template.TrimStart('\n').Replace("{{DRONES}}", drones)

    let writeFile (path: string) (missions: ScheduledMission list) =
        File.WriteAllText(path, generate missions)
        printfn "Wrote MAVLink F# launcher to: %s" path
