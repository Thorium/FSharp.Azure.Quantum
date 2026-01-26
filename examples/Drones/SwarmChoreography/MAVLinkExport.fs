/// MAVLink Export Module
/// 
/// Converts quantum-optimized drone formation assignments to MAVLink-compatible
/// formats for ArduPilot, PX4, and other autopilot systems.
/// 
/// Supports:
/// - QGroundControl mission plan (.plan JSON)
/// - MAVLink waypoint file (.waypoints)
/// - Python script using pymavlink/dronekit
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
type GeoCoordinate = {
    Latitude: float   // Decimal degrees
    Longitude: float  // Decimal degrees
    Altitude: float   // Meters above sea level (AMSL) or relative
}

/// Local NED (North-East-Down) position relative to home
type LocalPosition = {
    North: float  // Meters, positive = north
    East: float   // Meters, positive = east
    Down: float   // Meters, positive = down (negative = up)
}

/// MAVLink command types (subset relevant for choreography)
type MavCmd =
    | NavWaypoint           // MAV_CMD_NAV_WAYPOINT (16)
    | NavLoiterTime         // MAV_CMD_NAV_LOITER_TIME (19)
    | NavReturnToLaunch     // MAV_CMD_NAV_RETURN_TO_LAUNCH (20)
    | NavLand               // MAV_CMD_NAV_LAND (21)
    | NavTakeoff            // MAV_CMD_NAV_TAKEOFF (22)
    | NavLoiterToAlt        // MAV_CMD_NAV_LOITER_TO_ALT (31)
    | DoSetMode             // MAV_CMD_DO_SET_MODE (176)
    | DoSetServo            // MAV_CMD_DO_SET_SERVO (183)
    | DoSetRelay            // MAV_CMD_DO_SET_RELAY (181)
    | DoChangeSpeed         // MAV_CMD_DO_CHANGE_SPEED (178)
    | DoSetRoiLocation      // MAV_CMD_DO_SET_ROI_LOCATION (195)
    | DoSetLedColor         // Custom for LED control

/// MAVLink frame types
type MavFrame =
    | Global              // MAV_FRAME_GLOBAL (0) - WGS84 absolute
    | GlobalRelativeAlt   // MAV_FRAME_GLOBAL_RELATIVE_ALT (3) - WGS84 lat/lon, relative alt
    | LocalNed            // MAV_FRAME_LOCAL_NED (1) - North-East-Down relative to home
    | LocalEnu            // MAV_FRAME_LOCAL_ENU (4) - East-North-Up relative to home
    | Mission             // MAV_FRAME_MISSION (2) - Mission item specific

/// A single mission item in MAVLink format
type MissionItem = {
    Sequence: int
    Command: MavCmd
    Frame: MavFrame
    Param1: float   // Command-specific
    Param2: float   // Command-specific  
    Param3: float   // Command-specific
    Param4: float   // Yaw angle (degrees)
    Latitude: float
    Longitude: float
    Altitude: float
    Autocontinue: bool
}

/// Drone configuration for MAVLink systems
type MavDroneConfig = {
    SystemId: int          // MAVLink system ID (1-255)
    ComponentId: int       // MAVLink component ID (usually 1 for autopilot)
    Name: string
    ConnectionString: string  // e.g., "udp:127.0.0.1:14550" or "serial:/dev/ttyUSB0:57600"
}

/// Show metadata for MAVLink export
type MavShowMetadata = {
    Title: string
    GeneratedBy: string
    GeneratedAt: DateTimeOffset
    OptimizationMethod: string
    NumDrones: int
    TotalWaypoints: int
    HomePosition: GeoCoordinate option
}

/// Complete MAVLink mission for a single drone
type DroneMission = {
    Drone: MavDroneConfig
    HomePosition: GeoCoordinate
    Items: MissionItem list
}

/// Multi-drone swarm mission
type SwarmMission = {
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
    
    { Latitude = home.Latitude + local.North / metersPerDegreeLat
      Longitude = home.Longitude + local.East / metersPerDegreeLon
      Altitude = home.Altitude - local.Down }  // Down is negative altitude

/// Convert our Position3D (X=right, Y=forward, Z=up) to Local NED
let position3DToLocalNed (x: float) (y: float) (z: float) (scale: float) : LocalPosition =
    { North = y * scale   // Forward -> North
      East = x * scale    // Right -> East
      Down = -z * scale } // Up -> -Down

/// Convert our Position3D to geo coordinate relative to home
let position3DToGeo (home: GeoCoordinate) (x: float) (y: float) (z: float) (scale: float) : GeoCoordinate =
    let local = position3DToLocalNed x y z scale
    localToGeo home local

// =============================================================================
// MAV COMMAND ENCODING
// =============================================================================

/// Get MAVLink command ID
let private mavCmdId = function
    | NavWaypoint -> 16
    | NavLoiterTime -> 19
    | NavReturnToLaunch -> 20
    | NavLand -> 21
    | NavTakeoff -> 22
    | NavLoiterToAlt -> 31
    | DoSetMode -> 176
    | DoSetServo -> 183
    | DoSetRelay -> 181
    | DoChangeSpeed -> 178
    | DoSetRoiLocation -> 195
    | DoSetLedColor -> 999  // Custom extension

/// Get MAVLink frame ID
let private mavFrameId = function
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
    { Sequence = seq
      Command = cmd
      Frame = frame
      Param1 = 0.0
      Param2 = 0.0
      Param3 = 0.0
      Param4 = 0.0  // Yaw: 0 = don't change
      Latitude = lat
      Longitude = lon
      Altitude = alt
      Autocontinue = true }

/// Create a takeoff command
let takeoff (altitude: float) (home: GeoCoordinate) (seq: int) : MissionItem =
    { createItem seq NavTakeoff GlobalRelativeAlt home.Latitude home.Longitude altitude with
        Param1 = 0.0   // Pitch angle (ignored for multirotors)
        Param4 = 0.0 } // Yaw: 0 = maintain current heading

/// Create a waypoint command
let waypoint (holdTime: float) (acceptRadius: float) (pos: GeoCoordinate) (seq: int) : MissionItem =
    { createItem seq NavWaypoint GlobalRelativeAlt pos.Latitude pos.Longitude pos.Altitude with
        Param1 = holdTime       // Hold time in seconds
        Param2 = acceptRadius } // Acceptance radius in meters

/// Create a loiter (hover) command
let loiter (duration: float) (pos: GeoCoordinate) (seq: int) : MissionItem =
    { createItem seq NavLoiterTime GlobalRelativeAlt pos.Latitude pos.Longitude pos.Altitude with
        Param1 = duration }  // Loiter time in seconds

/// Create a land command
let landAt (pos: GeoCoordinate) (seq: int) : MissionItem =
    createItem seq NavLand GlobalRelativeAlt pos.Latitude pos.Longitude 0.0

/// Create a return-to-launch command
let returnToLaunch (seq: int) : MissionItem =
    createItem seq NavReturnToLaunch GlobalRelativeAlt 0.0 0.0 0.0

/// Create a speed change command
let setSpeed (speedMs: float) (seq: int) : MissionItem =
    { createItem seq DoChangeSpeed Mission 0.0 0.0 0.0 with
        Param1 = 1.0      // Speed type: 1 = ground speed
        Param2 = speedMs  // Speed in m/s
        Param3 = -1.0 }   // Throttle: -1 = no change

// =============================================================================
// MISSION BUILDING
// =============================================================================

/// Build mission items from formation waypoints
let buildMissionItems 
    (home: GeoCoordinate)
    (scale: float)
    (transitionSpeed: float)
    (formations: {| Name: string; Positions: {| X: float; Y: float; Z: float |}[] |}[])
    (droneIndex: int)
    : MissionItem list =
    
    let mutable seq = 0
    let nextSeq () = 
        let s = seq
        seq <- seq + 1
        s
    
    // Minimum safe takeoff altitude (meters)
    let minTakeoffAltitude = 2.0
    
    // Find a safe takeoff altitude from formations (skip ground-level positions)
    let firstAltitude = 
        formations 
        |> Array.tryPick (fun f -> 
            f.Positions 
            |> Array.tryItem droneIndex
            |> Option.bind (fun p -> 
                let alt = p.Z * scale
                if alt >= minTakeoffAltitude then Some alt else None))
        |> Option.defaultValue minTakeoffAltitude
    
    let items = ResizeArray<MissionItem>()
    
    // Takeoff
    items.Add(takeoff firstAltitude home (nextSeq()))
    
    // Set cruise speed
    items.Add(setSpeed transitionSpeed (nextSeq()))
    
    // Add waypoints for each formation
    for formation in formations do
        match formation.Positions |> Array.tryItem droneIndex with
        | Some pos ->
            let geoPos = position3DToGeo home pos.X pos.Y pos.Z scale
            // Waypoint with 2 second hold time, 0.5m acceptance radius
            items.Add(waypoint 2.0 0.5 geoPos (nextSeq()))
        | None -> ()
    
    // Return to launch
    items.Add(returnToLaunch (nextSeq()))
    
    items |> Seq.toList

/// Create a complete swarm mission from show data
let createSwarmMission
    (title: string)
    (homePosition: GeoCoordinate)
    (scale: float)
    (transitionSpeed: float)
    (formations: {| Name: string; Positions: {| X: float; Y: float; Z: float |}[] |}[])
    (numDrones: int)
    (optimizationMethod: string)
    : SwarmMission =
    
    let drones =
        [ for i in 0 .. numDrones - 1 ->
            { SystemId = i + 1
              ComponentId = 1
              Name = sprintf "Drone%d" i
              ConnectionString = sprintf "udp:127.0.0.1:%d" (14550 + i * 10) } ]
    
    let missions =
        drones
        |> List.mapi (fun i drone ->
            let items = buildMissionItems homePosition scale transitionSpeed formations i
            { Drone = drone
              HomePosition = homePosition
              Items = items })
    
    let totalWaypoints = missions |> List.sumBy (fun m -> m.Items.Length)
    
    { Metadata = 
        { Title = title
          GeneratedBy = "FSharp.Azure.Quantum SwarmChoreography"
          GeneratedAt = DateTimeOffset.UtcNow
          OptimizationMethod = optimizationMethod
          NumDrones = numDrones
          TotalWaypoints = totalWaypoints
          HomePosition = Some homePosition }
      Missions = missions }

// =============================================================================
// QGROUNDCONTROL PLAN EXPORT (.plan JSON)
// =============================================================================

module QGroundControl =
    
    /// Escape string for JSON
    let private escapeJson (s: string) =
        s.Replace("\\", "\\\\")
         .Replace("\"", "\\\"")
         .Replace("\n", "\\n")
         .Replace("\r", "\\r")
         .Replace("\t", "\\t")
    
    /// Format float for JSON (avoid locale issues)
    let private formatFloat (f: float) =
        if Double.IsNaN(f) then "null"
        elif Double.IsInfinity(f) then "null"
        else f.ToString("G15", System.Globalization.CultureInfo.InvariantCulture)
    
    /// Generate QGroundControl mission plan JSON for a single drone
    let toJson (mission: DroneMission) : string =
        let sb = StringBuilder()
        
        sb.AppendLine("{") |> ignore
        sb.AppendLine("  \"fileType\": \"Plan\",") |> ignore
        sb.AppendLine("  \"geoFence\": { \"circles\": [], \"polygons\": [], \"version\": 2 },") |> ignore
        sb.AppendLine("  \"groundStation\": \"FSharp.Azure.Quantum\",") |> ignore
        
        // Mission section
        sb.AppendLine("  \"mission\": {") |> ignore
        sb.AppendLine("    \"cruiseSpeed\": 5,") |> ignore
        sb.AppendLine("    \"firmwareType\": 3,") |> ignore  // 3 = ArduPilot
        sb.AppendLine("    \"globalPlanAltitudeMode\": 1,") |> ignore  // 1 = Relative
        sb.AppendLine("    \"hoverSpeed\": 3,") |> ignore
        
        // Items array
        sb.AppendLine("    \"items\": [") |> ignore
        
        let itemCount = mission.Items.Length
        for i, item in mission.Items |> List.indexed do
            sb.AppendLine("      {") |> ignore
            sb.AppendLine("        \"AMSLAltAboveTerrain\": null,") |> ignore
            sb.AppendLine(sprintf "        \"Altitude\": %s," (formatFloat item.Altitude)) |> ignore
            sb.AppendLine("        \"AltitudeMode\": 1,") |> ignore  // 1 = Relative
            sb.AppendLine(sprintf "        \"autoContinue\": %s," (if item.Autocontinue then "true" else "false")) |> ignore
            sb.AppendLine(sprintf "        \"command\": %d," (mavCmdId item.Command)) |> ignore
            sb.AppendLine(sprintf "        \"doJumpId\": %d," (i + 1)) |> ignore
            sb.AppendLine(sprintf "        \"frame\": %d," (mavFrameId item.Frame)) |> ignore
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
        sb.AppendLine(sprintf "      %s," (formatFloat mission.HomePosition.Latitude)) |> ignore
        sb.AppendLine(sprintf "      %s," (formatFloat mission.HomePosition.Longitude)) |> ignore
        sb.AppendLine(sprintf "      %s" (formatFloat mission.HomePosition.Altitude)) |> ignore
        sb.AppendLine("    ],") |> ignore
        sb.AppendLine("    \"vehicleType\": 2,") |> ignore  // 2 = Quadrotor
        sb.AppendLine("    \"version\": 2") |> ignore
        sb.AppendLine("  },") |> ignore
        
        // Rally points (empty)
        sb.AppendLine("  \"rallyPoints\": { \"points\": [], \"version\": 2 },") |> ignore
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
        sb.AppendLine(sprintf "0\t1\t0\t16\t0\t0\t0\t0\t%.8f\t%.8f\t%.2f\t1"
            mission.HomePosition.Latitude
            mission.HomePosition.Longitude
            mission.HomePosition.Altitude) |> ignore
        
        // Mission items
        for item in mission.Items do
            // Format: INDEX CURRENT_WP COORD_FRAME COMMAND P1 P2 P3 P4 LAT LON ALT AUTOCONTINUE
            let current = if item.Sequence = 1 then 1 else 0
            sb.AppendLine(sprintf "%d\t%d\t%d\t%d\t%.4f\t%.4f\t%.4f\t%.4f\t%.8f\t%.8f\t%.2f\t%d"
                item.Sequence
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
                (if item.Autocontinue then 1 else 0)) |> ignore
        
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
// PYMAVLINK PYTHON SCRIPT EXPORT
// =============================================================================

module PythonScript =
    
    /// Generate Python script using pymavlink/dronekit for swarm control
    let generate (swarm: SwarmMission) : string =
        let sb = StringBuilder()
        
        // Header and imports
        sb.AppendLine("#!/usr/bin/env python3") |> ignore
        sb.AppendLine("\"\"\"") |> ignore
        sb.AppendLine("MAVLink Swarm Choreography Script") |> ignore
        sb.AppendLine(sprintf "Generated by: %s" swarm.Metadata.GeneratedBy) |> ignore
        sb.AppendLine(sprintf "Generated at: %s" (swarm.Metadata.GeneratedAt.ToString("O"))) |> ignore
        sb.AppendLine(sprintf "Optimization: %s" swarm.Metadata.OptimizationMethod) |> ignore
        sb.AppendLine(sprintf "Drones: %d" swarm.Metadata.NumDrones) |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("Requirements:") |> ignore
        sb.AppendLine("  pip install pymavlink dronekit") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("Supported Autopilots:") |> ignore
        sb.AppendLine("  - ArduPilot (Copter 4.x+)") |> ignore
        sb.AppendLine("  - PX4 (1.12+)") |> ignore
        sb.AppendLine("  - Any MAVLink-compatible flight controller") |> ignore
        sb.AppendLine("\"\"\"") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("import time") |> ignore
        sb.AppendLine("import sys") |> ignore
        sb.AppendLine("import threading") |> ignore
        sb.AppendLine("from typing import List, Optional, Tuple") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("try:") |> ignore
        sb.AppendLine("    from dronekit import connect, VehicleMode, LocationGlobalRelative") |> ignore
        sb.AppendLine("    from dronekit import Command") |> ignore
        sb.AppendLine("    from pymavlink import mavutil") |> ignore
        sb.AppendLine("    DRONEKIT_AVAILABLE = True") |> ignore
        sb.AppendLine("except ImportError:") |> ignore
        sb.AppendLine("    DRONEKIT_AVAILABLE = False") |> ignore
        sb.AppendLine("    print('Warning: dronekit not installed. Install with: pip install dronekit')") |> ignore
        sb.AppendLine("") |> ignore
        
        // Configuration
        sb.AppendLine("# ==============================================================================") |> ignore
        sb.AppendLine("# CONFIGURATION") |> ignore
        sb.AppendLine("# ==============================================================================") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("# Drone connection strings (update for your setup)") |> ignore
        sb.AppendLine("# Examples:") |> ignore
        sb.AppendLine("#   Serial: '/dev/ttyUSB0' or 'COM3' (Windows)") |> ignore
        sb.AppendLine("#   UDP: 'udp:127.0.0.1:14550'") |> ignore
        sb.AppendLine("#   TCP: 'tcp:127.0.0.1:5760'") |> ignore
        sb.AppendLine("#   SITL: 'udp:127.0.0.1:14551'") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("DRONE_CONNECTIONS = [") |> ignore
        for mission in swarm.Missions do
            sb.AppendLine(sprintf "    {'name': '%s', 'connection': '%s', 'sysid': %d},"
                mission.Drone.Name
                mission.Drone.ConnectionString
                mission.Drone.SystemId) |> ignore
        sb.AppendLine("]") |> ignore
        sb.AppendLine("") |> ignore
        
        // Home position
        match swarm.Metadata.HomePosition with
        | Some home ->
            sb.AppendLine(sprintf "HOME_POSITION = (%.8f, %.8f, %.2f)  # (lat, lon, alt)" 
                home.Latitude home.Longitude home.Altitude) |> ignore
        | None ->
            sb.AppendLine("HOME_POSITION = None  # Use GPS position at arm time") |> ignore
        sb.AppendLine("") |> ignore
        
        // Safety parameters
        sb.AppendLine("# Safety parameters") |> ignore
        sb.AppendLine("TAKEOFF_ALTITUDE = 2.0  # meters") |> ignore
        sb.AppendLine("WAYPOINT_ACCEPTANCE_RADIUS = 0.5  # meters") |> ignore
        sb.AppendLine("MAX_GROUNDSPEED = 5.0  # m/s") |> ignore
        sb.AppendLine("CONNECTION_TIMEOUT = 30  # seconds") |> ignore
        sb.AppendLine("ARM_TIMEOUT = 10  # seconds") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("# Emergency stop flag") |> ignore
        sb.AppendLine("EMERGENCY_STOP = threading.Event()") |> ignore
        sb.AppendLine("") |> ignore
        
        // Waypoint data
        sb.AppendLine("# ==============================================================================") |> ignore
        sb.AppendLine("# WAYPOINT DATA (from quantum optimization)") |> ignore
        sb.AppendLine("# ==============================================================================") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("DRONE_WAYPOINTS = {") |> ignore
        for mission in swarm.Missions do
            sb.AppendLine(sprintf "    '%s': [" mission.Drone.Name) |> ignore
            for item in mission.Items do
                sb.AppendLine(sprintf "        {'cmd': %d, 'lat': %.8f, 'lon': %.8f, 'alt': %.2f, 'p1': %.2f, 'p2': %.2f},"
                    (mavCmdId item.Command)
                    item.Latitude
                    item.Longitude
                    item.Altitude
                    item.Param1
                    item.Param2) |> ignore
            sb.AppendLine("    ],") |> ignore
        sb.AppendLine("}") |> ignore
        sb.AppendLine("") |> ignore
        
        // Helper functions
        sb.AppendLine("# ==============================================================================") |> ignore
        sb.AppendLine("# HELPER FUNCTIONS") |> ignore
        sb.AppendLine("# ==============================================================================") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("def connect_drone(connection_string: str, timeout: int = CONNECTION_TIMEOUT):") |> ignore
        sb.AppendLine("    \"\"\"Connect to drone with timeout and return vehicle object.\"\"\"") |> ignore
        sb.AppendLine("    if not DRONEKIT_AVAILABLE:") |> ignore
        sb.AppendLine("        raise RuntimeError('dronekit not available')") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    print(f'Connecting to {connection_string}...')") |> ignore
        sb.AppendLine("    vehicle = connect(connection_string, wait_ready=True, timeout=timeout)") |> ignore
        sb.AppendLine("    print(f'  Connected. Firmware: {vehicle.version}')") |> ignore
        sb.AppendLine("    return vehicle") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("def arm_and_takeoff(vehicle, target_altitude: float):") |> ignore
        sb.AppendLine("    \"\"\"Arms vehicle and flies to target altitude.\"\"\"") |> ignore
        sb.AppendLine("    print(f'Arming and taking off to {target_altitude}m...')") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    # Pre-arm checks") |> ignore
        sb.AppendLine("    while not vehicle.is_armable:") |> ignore
        sb.AppendLine("        if EMERGENCY_STOP.is_set():") |> ignore
        sb.AppendLine("            return False") |> ignore
        sb.AppendLine("        print('  Waiting for vehicle to become armable...')") |> ignore
        sb.AppendLine("        time.sleep(1)") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    # Set mode to GUIDED") |> ignore
        sb.AppendLine("    vehicle.mode = VehicleMode('GUIDED')") |> ignore
        sb.AppendLine("    while vehicle.mode.name != 'GUIDED':") |> ignore
        sb.AppendLine("        if EMERGENCY_STOP.is_set():") |> ignore
        sb.AppendLine("            return False") |> ignore
        sb.AppendLine("        time.sleep(0.5)") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    # Arm the vehicle") |> ignore
        sb.AppendLine("    vehicle.armed = True") |> ignore
        sb.AppendLine("    start_time = time.time()") |> ignore
        sb.AppendLine("    while not vehicle.armed:") |> ignore
        sb.AppendLine("        if EMERGENCY_STOP.is_set():") |> ignore
        sb.AppendLine("            return False") |> ignore
        sb.AppendLine("        if time.time() - start_time > ARM_TIMEOUT:") |> ignore
        sb.AppendLine("            print('  ERROR: Arm timeout')") |> ignore
        sb.AppendLine("            return False") |> ignore
        sb.AppendLine("        time.sleep(0.5)") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    print('  Armed. Taking off...')") |> ignore
        sb.AppendLine("    vehicle.simple_takeoff(target_altitude)") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    # Wait until target altitude reached") |> ignore
        sb.AppendLine("    while True:") |> ignore
        sb.AppendLine("        if EMERGENCY_STOP.is_set():") |> ignore
        sb.AppendLine("            return False") |> ignore
        sb.AppendLine("        current_alt = vehicle.location.global_relative_frame.alt") |> ignore
        sb.AppendLine("        print(f'  Altitude: {current_alt:.1f}m')") |> ignore
        sb.AppendLine("        if current_alt >= target_altitude * 0.95:") |> ignore
        sb.AppendLine("            print('  Reached target altitude')") |> ignore
        sb.AppendLine("            return True") |> ignore
        sb.AppendLine("        time.sleep(1)") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("def goto_position(vehicle, lat: float, lon: float, alt: float):") |> ignore
        sb.AppendLine("    \"\"\"Command vehicle to fly to position.\"\"\"") |> ignore
        sb.AppendLine("    if EMERGENCY_STOP.is_set():") |> ignore
        sb.AppendLine("        return") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    target = LocationGlobalRelative(lat, lon, alt)") |> ignore
        sb.AppendLine("    vehicle.simple_goto(target, groundspeed=MAX_GROUNDSPEED)") |> ignore
        sb.AppendLine("    print(f'  Going to ({lat:.6f}, {lon:.6f}, {alt:.1f}m)')") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("def wait_for_position(vehicle, lat: float, lon: float, radius: float = WAYPOINT_ACCEPTANCE_RADIUS, timeout: float = 60):") |> ignore
        sb.AppendLine("    \"\"\"Wait until vehicle reaches position within acceptance radius.\"\"\"") |> ignore
        sb.AppendLine("    from math import radians, sin, cos, sqrt, atan2") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    def haversine(lat1, lon1, lat2, lon2):") |> ignore
        sb.AppendLine("        R = 6371000  # Earth radius in meters") |> ignore
        sb.AppendLine("        phi1, phi2 = radians(lat1), radians(lat2)") |> ignore
        sb.AppendLine("        dphi = radians(lat2 - lat1)") |> ignore
        sb.AppendLine("        dlambda = radians(lon2 - lon1)") |> ignore
        sb.AppendLine("        a = sin(dphi/2)**2 + cos(phi1)*cos(phi2)*sin(dlambda/2)**2") |> ignore
        sb.AppendLine("        return 2 * R * atan2(sqrt(a), sqrt(1-a))") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    start_time = time.time()") |> ignore
        sb.AppendLine("    while True:") |> ignore
        sb.AppendLine("        if EMERGENCY_STOP.is_set():") |> ignore
        sb.AppendLine("            return False") |> ignore
        sb.AppendLine("        ") |> ignore
        sb.AppendLine("        current = vehicle.location.global_relative_frame") |> ignore
        sb.AppendLine("        distance = haversine(current.lat, current.lon, lat, lon)") |> ignore
        sb.AppendLine("        ") |> ignore
        sb.AppendLine("        if distance <= radius:") |> ignore
        sb.AppendLine("            return True") |> ignore
        sb.AppendLine("        ") |> ignore
        sb.AppendLine("        if time.time() - start_time > timeout:") |> ignore
        sb.AppendLine("            print(f'  WARNING: Waypoint timeout (distance: {distance:.1f}m)')") |> ignore
        sb.AppendLine("            return False") |> ignore
        sb.AppendLine("        ") |> ignore
        sb.AppendLine("        time.sleep(0.5)") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("def land_vehicle(vehicle):") |> ignore
        sb.AppendLine("    \"\"\"Land the vehicle.\"\"\"") |> ignore
        sb.AppendLine("    print('  Landing...')") |> ignore
        sb.AppendLine("    vehicle.mode = VehicleMode('LAND')") |> ignore
        sb.AppendLine("    while vehicle.armed:") |> ignore
        sb.AppendLine("        if EMERGENCY_STOP.is_set():") |> ignore
        sb.AppendLine("            break") |> ignore
        sb.AppendLine("        time.sleep(1)") |> ignore
        sb.AppendLine("    print('  Landed and disarmed')") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("def emergency_stop_all(vehicles: list):") |> ignore
        sb.AppendLine("    \"\"\"Emergency stop all vehicles.\"\"\"") |> ignore
        sb.AppendLine("    print('\\n!!! EMERGENCY STOP !!!')") |> ignore
        sb.AppendLine("    EMERGENCY_STOP.set()") |> ignore
        sb.AppendLine("    for v in vehicles:") |> ignore
        sb.AppendLine("        try:") |> ignore
        sb.AppendLine("            v.mode = VehicleMode('LAND')") |> ignore
        sb.AppendLine("        except:") |> ignore
        sb.AppendLine("            pass") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("") |> ignore
        
        // Single drone mission execution
        sb.AppendLine("def execute_drone_mission(drone_config: dict, waypoints: list):") |> ignore
        sb.AppendLine("    \"\"\"Execute mission for a single drone.\"\"\"") |> ignore
        sb.AppendLine("    name = drone_config['name']") |> ignore
        sb.AppendLine("    connection = drone_config['connection']") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    print(f'\\n=== {name} Starting Mission ===')") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    try:") |> ignore
        sb.AppendLine("        vehicle = connect_drone(connection)") |> ignore
        sb.AppendLine("        ") |> ignore
        sb.AppendLine("        # Get first waypoint altitude for takeoff") |> ignore
        sb.AppendLine("        takeoff_alt = TAKEOFF_ALTITUDE") |> ignore
        sb.AppendLine("        for wp in waypoints:") |> ignore
        sb.AppendLine("            if wp['cmd'] == 22:  # NAV_TAKEOFF") |> ignore
        sb.AppendLine("                takeoff_alt = wp['alt']") |> ignore
        sb.AppendLine("                break") |> ignore
        sb.AppendLine("        ") |> ignore
        sb.AppendLine("        if not arm_and_takeoff(vehicle, takeoff_alt):") |> ignore
        sb.AppendLine("            print(f'{name}: Failed to arm/takeoff')") |> ignore
        sb.AppendLine("            return False") |> ignore
        sb.AppendLine("        ") |> ignore
        sb.AppendLine("        # Execute waypoints") |> ignore
        sb.AppendLine("        for i, wp in enumerate(waypoints):") |> ignore
        sb.AppendLine("            if EMERGENCY_STOP.is_set():") |> ignore
        sb.AppendLine("                break") |> ignore
        sb.AppendLine("            ") |> ignore
        sb.AppendLine("            cmd = wp['cmd']") |> ignore
        sb.AppendLine("            ") |> ignore
        sb.AppendLine("            if cmd == 16:  # NAV_WAYPOINT") |> ignore
        sb.AppendLine("                print(f'{name}: Waypoint {i+1}/{len(waypoints)}')") |> ignore
        sb.AppendLine("                goto_position(vehicle, wp['lat'], wp['lon'], wp['alt'])") |> ignore
        sb.AppendLine("                wait_for_position(vehicle, wp['lat'], wp['lon'])") |> ignore
        sb.AppendLine("                # Hold time") |> ignore
        sb.AppendLine("                if wp.get('p1', 0) > 0:") |> ignore
        sb.AppendLine("                    time.sleep(wp['p1'])") |> ignore
        sb.AppendLine("            ") |> ignore
        sb.AppendLine("            elif cmd == 19:  # NAV_LOITER_TIME") |> ignore
        sb.AppendLine("                print(f'{name}: Loitering for {wp[\"p1\"]}s')") |> ignore
        sb.AppendLine("                goto_position(vehicle, wp['lat'], wp['lon'], wp['alt'])") |> ignore
        sb.AppendLine("                wait_for_position(vehicle, wp['lat'], wp['lon'])") |> ignore
        sb.AppendLine("                time.sleep(wp['p1'])") |> ignore
        sb.AppendLine("            ") |> ignore
        sb.AppendLine("            elif cmd == 20:  # NAV_RETURN_TO_LAUNCH") |> ignore
        sb.AppendLine("                print(f'{name}: Returning to launch')") |> ignore
        sb.AppendLine("                vehicle.mode = VehicleMode('RTL')") |> ignore
        sb.AppendLine("                while vehicle.armed:") |> ignore
        sb.AppendLine("                    if EMERGENCY_STOP.is_set():") |> ignore
        sb.AppendLine("                        break") |> ignore
        sb.AppendLine("                    time.sleep(1)") |> ignore
        sb.AppendLine("            ") |> ignore
        sb.AppendLine("            elif cmd == 21:  # NAV_LAND") |> ignore
        sb.AppendLine("                land_vehicle(vehicle)") |> ignore
        sb.AppendLine("            ") |> ignore
        sb.AppendLine("            elif cmd == 22:  # NAV_TAKEOFF") |> ignore
        sb.AppendLine("                pass  # Already handled above") |> ignore
        sb.AppendLine("            ") |> ignore
        sb.AppendLine("            elif cmd == 178:  # DO_CHANGE_SPEED") |> ignore
        sb.AppendLine("                vehicle.groundspeed = wp['p2']") |> ignore
        sb.AppendLine("        ") |> ignore
        sb.AppendLine("        print(f'{name}: Mission complete')") |> ignore
        sb.AppendLine("        vehicle.close()") |> ignore
        sb.AppendLine("        return True") |> ignore
        sb.AppendLine("        ") |> ignore
        sb.AppendLine("    except Exception as e:") |> ignore
        sb.AppendLine("        print(f'{name}: Error - {e}')") |> ignore
        sb.AppendLine("        return False") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("") |> ignore
        
        // Main function
        sb.AppendLine("# ==============================================================================") |> ignore
        sb.AppendLine("# MAIN EXECUTION") |> ignore
        sb.AppendLine("# ==============================================================================") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("def main():") |> ignore
        sb.AppendLine("    \"\"\"Main entry point for swarm choreography.\"\"\"") |> ignore
        sb.AppendLine("    if not DRONEKIT_AVAILABLE:") |> ignore
        sb.AppendLine("        print('Error: dronekit is required. Install with:')") |> ignore
        sb.AppendLine("        print('  pip install dronekit pymavlink')") |> ignore
        sb.AppendLine("        sys.exit(1)") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    print('=' * 60)") |> ignore
        sb.AppendLine(sprintf "    print('MAVLink Swarm Choreography - %d Drones')" swarm.Metadata.NumDrones) |> ignore
        sb.AppendLine(sprintf "    print('Optimization: %s')" swarm.Metadata.OptimizationMethod) |> ignore
        sb.AppendLine("    print('=' * 60)") |> ignore
        sb.AppendLine("    print()") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    # For single-threaded execution (safer for testing)") |> ignore
        sb.AppendLine("    for drone_config in DRONE_CONNECTIONS:") |> ignore
        sb.AppendLine("        waypoints = DRONE_WAYPOINTS.get(drone_config['name'], [])") |> ignore
        sb.AppendLine("        if waypoints:") |> ignore
        sb.AppendLine("            execute_drone_mission(drone_config, waypoints)") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    print()") |> ignore
        sb.AppendLine("    print('All missions complete!')") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("def main_parallel():") |> ignore
        sb.AppendLine("    \"\"\"Execute all drone missions in parallel (for synchronized swarm).\"\"\"") |> ignore
        sb.AppendLine("    if not DRONEKIT_AVAILABLE:") |> ignore
        sb.AppendLine("        print('Error: dronekit is required')") |> ignore
        sb.AppendLine("        sys.exit(1)") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    threads = []") |> ignore
        sb.AppendLine("    for drone_config in DRONE_CONNECTIONS:") |> ignore
        sb.AppendLine("        waypoints = DRONE_WAYPOINTS.get(drone_config['name'], [])") |> ignore
        sb.AppendLine("        if waypoints:") |> ignore
        sb.AppendLine("            t = threading.Thread(") |> ignore
        sb.AppendLine("                target=execute_drone_mission,") |> ignore
        sb.AppendLine("                args=(drone_config, waypoints),") |> ignore
        sb.AppendLine("                name=drone_config['name']") |> ignore
        sb.AppendLine("            )") |> ignore
        sb.AppendLine("            threads.append(t)") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    # Start all threads") |> ignore
        sb.AppendLine("    for t in threads:") |> ignore
        sb.AppendLine("        t.start()") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    # Wait for completion") |> ignore
        sb.AppendLine("    for t in threads:") |> ignore
        sb.AppendLine("        t.join()") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    print('All parallel missions complete!')") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("") |> ignore
        sb.AppendLine("if __name__ == '__main__':") |> ignore
        sb.AppendLine("    import argparse") |> ignore
        sb.AppendLine("    parser = argparse.ArgumentParser(description='MAVLink Swarm Choreography')") |> ignore
        sb.AppendLine("    parser.add_argument('--parallel', action='store_true', help='Run drones in parallel')") |> ignore
        sb.AppendLine("    args = parser.parse_args()") |> ignore
        sb.AppendLine("    ") |> ignore
        sb.AppendLine("    if args.parallel:") |> ignore
        sb.AppendLine("        main_parallel()") |> ignore
        sb.AppendLine("    else:") |> ignore
        sb.AppendLine("        main()") |> ignore
        
        sb.ToString()
    
    /// Write Python script to file
    let writeFile (path: string) (swarm: SwarmMission) =
        File.WriteAllText(path, generate swarm)
        printfn "Wrote MAVLink Python script to: %s" path

// =============================================================================
// HIGH-LEVEL EXPORT API
// =============================================================================

/// Export a complete show to all MAVLink formats
let exportShow
    (baseDir: string)
    (title: string)
    (homePosition: GeoCoordinate)
    (scale: float)
    (transitionSpeed: float)
    (formations: {| Name: string; Positions: {| X: float; Y: float; Z: float |}[] |}[])
    (numDrones: int)
    (optimizationMethod: string)
    : unit =
    
    // Create swarm mission
    let swarm = createSwarmMission title homePosition scale transitionSpeed formations numDrones optimizationMethod
    
    // Ensure output directory exists
    Directory.CreateDirectory(baseDir) |> ignore
    
    // Export all formats
    QGroundControl.writeSwarmFiles baseDir swarm
    WaypointFile.writeSwarmFiles baseDir swarm
    PythonScript.writeFile (Path.Combine(baseDir, "mavlink_swarm.py")) swarm
    
    printfn ""
    printfn "MAVLink export complete!"
    printfn "  - QGroundControl plans: %d files" swarm.Missions.Length
    printfn "  - Waypoint files: %d files" swarm.Missions.Length
    printfn "  - Python script: mavlink_swarm.py"

/// Convenience function to create show from transition results (matches CrazyflieExport API)
let fromTransitionResults
    (transitions: {| FromFormation: string; ToFormation: string; Assignments: {| DroneId: int; TargetPositionIndex: int |}[]; TotalDistance: float; Method: string |}[])
    (formations: {| Name: string; Positions: {| X: float; Y: float; Z: float |}[] |}[])
    (homePosition: GeoCoordinate)
    (scale: float)
    (transitionSpeed: float)
    : SwarmMission =
    
    let numDrones = 
        formations 
        |> Array.tryHead 
        |> Option.map (fun f -> f.Positions.Length) 
        |> Option.defaultValue 4
    
    let quantumCount = transitions |> Array.filter (fun t -> t.Method.Contains("Quantum")) |> Array.length
    let optimizationMethod = 
        if quantumCount > transitions.Length / 2 then "Quantum (QAOA)"
        elif quantumCount > 0 then "Hybrid (Quantum + Classical)"
        else "Classical (Greedy)"
    
    createSwarmMission 
        "Quantum Drone Choreography" 
        homePosition 
        scale 
        transitionSpeed 
        formations 
        numDrones 
        optimizationMethod
