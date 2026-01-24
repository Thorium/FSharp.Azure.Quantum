/// Crazyflie Export Module
/// 
/// Converts quantum-optimized drone formation assignments to executable
/// Crazyflie Python scripts for real swarm automation.
/// 
/// Supports:
/// - Crazyflie 2.1 with Lighthouse/Loco positioning
/// - cflib high-level commander API
/// - JSON waypoint export
/// - Direct Python script generation
module FSharp.Azure.Quantum.Examples.Drone.SwarmChoreography.CrazyflieExport

open System
open System.IO
open System.Text

// =============================================================================
// TYPES
// =============================================================================

/// Position in Crazyflie coordinate system (meters, relative to origin)
type CrazyfliePosition = {
    X: float  // meters, forward
    Y: float  // meters, left
    Z: float  // meters, up (altitude)
}

/// LED color for light shows
type LedColor = {
    R: int  // 0-255
    G: int  // 0-255
    B: int  // 0-255
}

/// A single waypoint for one drone
type Waypoint = {
    DroneId: int
    Position: CrazyfliePosition
    Color: LedColor option
    Duration: float  // seconds to reach this position
}

/// A formation consisting of waypoints for all drones at a specific time
type FormationWaypoints = {
    Name: string
    TimestampMs: int
    Waypoints: Waypoint[]
}

/// Complete show definition
type ShowDefinition = {
    Metadata: ShowMetadata
    Drones: DroneConfig[]
    Formations: FormationWaypoints[]
}

and ShowMetadata = {
    GeneratedBy: string
    GeneratedAt: DateTimeOffset
    OptimizationMethod: string
    NumDrones: int
    TotalTransitions: int
    TotalDistanceMeters: float
}

and DroneConfig = {
    Id: int
    Uri: string  // e.g., "radio://0/80/2M/E7E7E7E700"
    Name: string option
}

// =============================================================================
// COORDINATE CONVERSION
// =============================================================================

/// Convert our Position3D (X=right, Y=forward, Z=up) to Crazyflie (X=forward, Y=left, Z=up)
/// and scale for indoor use
let convertPosition (pos: {| X: float; Y: float; Z: float |}) (scale: float) : CrazyfliePosition =
    {
        X = pos.Y * scale    // Our Y (forward) -> CF X (forward)
        Y = -pos.X * scale   // Our X (right) -> CF -Y (left)
        Z = pos.Z * scale    // Both Z = up
    }

/// Convert our internal Position3D type
let convertPosition3D (x: float) (y: float) (z: float) (scale: float) : CrazyfliePosition =
    {
        X = y * scale
        Y = -x * scale
        Z = z * scale
    }

// =============================================================================
// DEFAULT CONFIGURATIONS
// =============================================================================

/// Default drone URIs for 4-drone swarm
let defaultDroneUris = [|
    "radio://0/80/2M/E7E7E7E700"
    "radio://0/80/2M/E7E7E7E701"
    "radio://0/80/2M/E7E7E7E702"
    "radio://0/80/2M/E7E7E7E703"
|]

/// Default colors for drones (for LED shows)
let defaultColors = [|
    { R = 255; G = 0; B = 0 }     // Red
    { R = 0; G = 255; B = 0 }     // Green
    { R = 0; G = 0; B = 255 }     // Blue
    { R = 255; G = 255; B = 0 }   // Yellow
|]

// =============================================================================
// JSON EXPORT
// =============================================================================

/// Escape string for JSON
let private escapeJson (s: string) =
    s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r")

/// Convert show definition to JSON string
let toJson (show: ShowDefinition) : string =
    let sb = StringBuilder()
    
    sb.AppendLine("{") |> ignore
    
    // Metadata
    sb.AppendLine("  \"metadata\": {") |> ignore
    sb.AppendLine($"    \"generated_by\": \"{escapeJson show.Metadata.GeneratedBy}\",") |> ignore
    sb.AppendLine($"    \"generated_at\": \"{show.Metadata.GeneratedAt:O}\",") |> ignore
    sb.AppendLine($"    \"optimization_method\": \"{escapeJson show.Metadata.OptimizationMethod}\",") |> ignore
    sb.AppendLine($"    \"num_drones\": {show.Metadata.NumDrones},") |> ignore
    sb.AppendLine($"    \"total_transitions\": {show.Metadata.TotalTransitions},") |> ignore
    sb.AppendLine($"    \"total_distance_meters\": {show.Metadata.TotalDistanceMeters:F2}") |> ignore
    sb.AppendLine("  },") |> ignore
    
    // Drones
    sb.AppendLine("  \"drones\": [") |> ignore
    for i, drone in Array.indexed show.Drones do
        let comma = if i < show.Drones.Length - 1 then "," else ""
        let name = match drone.Name with Some n -> $", \"name\": \"{escapeJson n}\"" | None -> ""
        sb.AppendLine($"    {{ \"id\": {drone.Id}, \"uri\": \"{drone.Uri}\"{name} }}{comma}") |> ignore
    sb.AppendLine("  ],") |> ignore
    
    // Formations
    sb.AppendLine("  \"formations\": [") |> ignore
    for fi, formation in Array.indexed show.Formations do
        sb.AppendLine("    {") |> ignore
        sb.AppendLine($"      \"name\": \"{escapeJson formation.Name}\",") |> ignore
        sb.AppendLine($"      \"timestamp_ms\": {formation.TimestampMs},") |> ignore
        sb.AppendLine("      \"waypoints\": [") |> ignore
        for wi, wp in Array.indexed formation.Waypoints do
            let wcomma = if wi < formation.Waypoints.Length - 1 then "," else ""
            let colorStr = 
                match wp.Color with
                | Some c -> $", \"color\": {{ \"r\": {c.R}, \"g\": {c.G}, \"b\": {c.B} }}"
                | None -> ""
            sb.AppendLine($"        {{ \"drone_id\": {wp.DroneId}, \"x\": {wp.Position.X:F3}, \"y\": {wp.Position.Y:F3}, \"z\": {wp.Position.Z:F3}, \"duration\": {wp.Duration:F1}{colorStr} }}{wcomma}") |> ignore
        sb.AppendLine("      ]") |> ignore
        let fcomma = if fi < show.Formations.Length - 1 then "," else ""
        sb.AppendLine($"    }}{fcomma}") |> ignore
    sb.AppendLine("  ]") |> ignore
    
    sb.AppendLine("}") |> ignore
    sb.ToString()

/// Write JSON to file
let writeJson (path: string) (show: ShowDefinition) =
    File.WriteAllText(path, toJson show)
    printfn "Wrote JSON waypoints to: %s" path

// =============================================================================
// PYTHON SCRIPT GENERATION
// =============================================================================

/// Generate executable Python script for Crazyflie swarm
let toPythonScript (show: ShowDefinition) : string =
    let sb = StringBuilder()
    
    // Header
    sb.AppendLine("#!/usr/bin/env python3") |> ignore
    sb.AppendLine("\"\"\"") |> ignore
    sb.AppendLine("Crazyflie Swarm Choreography Script") |> ignore
    sb.AppendLine($"Generated by: {show.Metadata.GeneratedBy}") |> ignore
    sb.AppendLine($"Generated at: {show.Metadata.GeneratedAt:O}") |> ignore
    sb.AppendLine($"Optimization: {show.Metadata.OptimizationMethod}") |> ignore
    sb.AppendLine($"Drones: {show.Metadata.NumDrones}") |> ignore
    sb.AppendLine($"Total distance: {show.Metadata.TotalDistanceMeters:F2} meters") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("Requirements:") |> ignore
    sb.AppendLine("  pip install cflib") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("Hardware:") |> ignore
    sb.AppendLine("  - Crazyflie 2.1 drones with Lighthouse/Loco positioning") |> ignore
    sb.AppendLine("  - Crazyradio PA USB dongle") |> ignore
    sb.AppendLine("  - Lighthouse base stations OR Loco positioning anchors") |> ignore
    sb.AppendLine("\"\"\"") |> ignore
    sb.AppendLine("") |> ignore
    
    // Imports
    sb.AppendLine("import time") |> ignore
    sb.AppendLine("import logging") |> ignore
    sb.AppendLine("from threading import Event") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("import cflib.crtp") |> ignore
    sb.AppendLine("from cflib.crazyflie import Crazyflie") |> ignore
    sb.AppendLine("from cflib.crazyflie.log import LogConfig") |> ignore
    sb.AppendLine("from cflib.crazyflie.syncCrazyflie import SyncCrazyflie") |> ignore
    sb.AppendLine("from cflib.crazyflie.syncLogger import SyncLogger") |> ignore
    sb.AppendLine("from cflib.positioning.motion_commander import MotionCommander") |> ignore
    sb.AppendLine("from cflib.crazyflie.swarm import CachedCfFactory, Swarm") |> ignore
    sb.AppendLine("from cflib.crazyflie.mem import MemoryElement") |> ignore
    sb.AppendLine("from cflib.crazyflie.mem import Poly4D") |> ignore
    sb.AppendLine("") |> ignore
    
    // Configuration
    sb.AppendLine("# ==============================================================================") |> ignore
    sb.AppendLine("# CONFIGURATION") |> ignore
    sb.AppendLine("# ==============================================================================") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("# Drone URIs - update these to match your Crazyflies") |> ignore
    sb.AppendLine("DRONE_URIS = [") |> ignore
    for drone in show.Drones do
        sb.AppendLine($"    '{drone.Uri}',  # Drone {drone.Id}") |> ignore
    sb.AppendLine("]") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("# Flight parameters") |> ignore
    sb.AppendLine("TAKEOFF_HEIGHT = 0.5  # meters") |> ignore
    sb.AppendLine("TAKEOFF_DURATION = 2.0  # seconds") |> ignore
    sb.AppendLine("LANDING_DURATION = 2.0  # seconds") |> ignore
    sb.AppendLine("DEFAULT_VELOCITY = 0.3  # m/s") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("# Safety parameters") |> ignore
    sb.AppendLine("MAX_HEIGHT = 2.0  # meters - adjust for your space") |> ignore
    sb.AppendLine("MIN_HEIGHT = 0.2  # meters") |> ignore
    sb.AppendLine("EMERGENCY_STOP = Event()") |> ignore
    sb.AppendLine("") |> ignore
    
    // Waypoints data
    sb.AppendLine("# ==============================================================================") |> ignore
    sb.AppendLine("# WAYPOINTS (generated from quantum optimization)") |> ignore
    sb.AppendLine("# ==============================================================================") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("FORMATIONS = {") |> ignore
    for formation in show.Formations do
        sb.AppendLine($"    '{formation.Name}': {{") |> ignore
        sb.AppendLine($"        'timestamp_ms': {formation.TimestampMs},") |> ignore
        sb.AppendLine("        'waypoints': {") |> ignore
        for wp in formation.Waypoints do
            let colorStr = 
                match wp.Color with
                | Some c -> $", 'color': ({c.R}, {c.G}, {c.B})"
                | None -> ""
            sb.AppendLine($"            {wp.DroneId}: {{'x': {wp.Position.X:F3}, 'y': {wp.Position.Y:F3}, 'z': {wp.Position.Z:F3}, 'duration': {wp.Duration:F1}{colorStr}}},") |> ignore
        sb.AppendLine("        }") |> ignore
        sb.AppendLine("    },") |> ignore
    sb.AppendLine("}") |> ignore
    sb.AppendLine("") |> ignore
    
    // Formation sequence
    sb.AppendLine("# Formation sequence (order of execution)") |> ignore
    sb.AppendLine("FORMATION_SEQUENCE = [") |> ignore
    for formation in show.Formations do
        sb.AppendLine($"    '{formation.Name}',") |> ignore
    sb.AppendLine("]") |> ignore
    sb.AppendLine("") |> ignore
    
    // Helper functions
    sb.AppendLine("# ==============================================================================") |> ignore
    sb.AppendLine("# HELPER FUNCTIONS") |> ignore
    sb.AppendLine("# ==============================================================================") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("def wait_for_position_estimator(scf):") |> ignore
    sb.AppendLine("    \"\"\"Wait for the position estimator to have a valid position.\"\"\"") |> ignore
    sb.AppendLine("    print(f'Waiting for position estimate for {scf.cf.link_uri}...')") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    log_config = LogConfig(name='Position', period_in_ms=100)") |> ignore
    sb.AppendLine("    log_config.add_variable('stateEstimate.x', 'float')") |> ignore
    sb.AppendLine("    log_config.add_variable('stateEstimate.y', 'float')") |> ignore
    sb.AppendLine("    log_config.add_variable('stateEstimate.z', 'float')") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    with SyncLogger(scf, log_config) as logger:") |> ignore
    sb.AppendLine("        for log_entry in logger:") |> ignore
    sb.AppendLine("            x = log_entry[1]['stateEstimate.x']") |> ignore
    sb.AppendLine("            y = log_entry[1]['stateEstimate.y']") |> ignore
    sb.AppendLine("            z = log_entry[1]['stateEstimate.z']") |> ignore
    sb.AppendLine("            ") |> ignore
    sb.AppendLine("            # Check if we have a reasonable position") |> ignore
    sb.AppendLine("            if abs(x) < 10 and abs(y) < 10 and abs(z) < 5:") |> ignore
    sb.AppendLine("                print(f'  Position found: ({x:.2f}, {y:.2f}, {z:.2f})')") |> ignore
    sb.AppendLine("                return True") |> ignore
    sb.AppendLine("            ") |> ignore
    sb.AppendLine("            if EMERGENCY_STOP.is_set():") |> ignore
    sb.AppendLine("                return False") |> ignore
    sb.AppendLine("    return False") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("def reset_estimator(scf):") |> ignore
    sb.AppendLine("    \"\"\"Reset the position estimator.\"\"\"") |> ignore
    sb.AppendLine("    cf = scf.cf") |> ignore
    sb.AppendLine("    cf.param.set_value('kalman.resetEstimation', '1')") |> ignore
    sb.AppendLine("    time.sleep(0.1)") |> ignore
    sb.AppendLine("    cf.param.set_value('kalman.resetEstimation', '0')") |> ignore
    sb.AppendLine("    wait_for_position_estimator(scf)") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("def set_led_color(cf, r, g, b):") |> ignore
    sb.AppendLine("    \"\"\"Set LED ring color (requires LED ring deck).\"\"\"") |> ignore
    sb.AppendLine("    try:") |> ignore
    sb.AppendLine("        cf.param.set_value('ring.effect', '7')  # Solid color effect") |> ignore
    sb.AppendLine("        cf.param.set_value('ring.solidRed', str(r))") |> ignore
    sb.AppendLine("        cf.param.set_value('ring.solidGreen', str(g))") |> ignore
    sb.AppendLine("        cf.param.set_value('ring.solidBlue', str(b))") |> ignore
    sb.AppendLine("    except Exception as e:") |> ignore
    sb.AppendLine("        pass  # LED ring not installed") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("") |> ignore
    
    // Swarm execution functions
    sb.AppendLine("# ==============================================================================") |> ignore
    sb.AppendLine("# SWARM EXECUTION") |> ignore
    sb.AppendLine("# ==============================================================================") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("def takeoff(scf, height=TAKEOFF_HEIGHT, duration=TAKEOFF_DURATION):") |> ignore
    sb.AppendLine("    \"\"\"Takeoff to specified height.\"\"\"") |> ignore
    sb.AppendLine("    cf = scf.cf") |> ignore
    sb.AppendLine("    commander = cf.high_level_commander") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    commander.takeoff(height, duration)") |> ignore
    sb.AppendLine("    time.sleep(duration + 0.5)") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("def land(scf, duration=LANDING_DURATION):") |> ignore
    sb.AppendLine("    \"\"\"Land the drone.\"\"\"") |> ignore
    sb.AppendLine("    cf = scf.cf") |> ignore
    sb.AppendLine("    commander = cf.high_level_commander") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    commander.land(0.0, duration)") |> ignore
    sb.AppendLine("    time.sleep(duration + 0.5)") |> ignore
    sb.AppendLine("    commander.stop()") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("def go_to_position(scf, x, y, z, duration, color=None):") |> ignore
    sb.AppendLine("    \"\"\"") |> ignore
    sb.AppendLine("    Move drone to absolute position.") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    Args:") |> ignore
    sb.AppendLine("        scf: SyncCrazyflie instance") |> ignore
    sb.AppendLine("        x, y, z: Target position in meters") |> ignore
    sb.AppendLine("        duration: Time to reach position in seconds") |> ignore
    sb.AppendLine("        color: Optional (r, g, b) tuple for LED") |> ignore
    sb.AppendLine("    \"\"\"") |> ignore
    sb.AppendLine("    if EMERGENCY_STOP.is_set():") |> ignore
    sb.AppendLine("        return") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    cf = scf.cf") |> ignore
    sb.AppendLine("    commander = cf.high_level_commander") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    # Clamp height for safety") |> ignore
    sb.AppendLine("    z = max(MIN_HEIGHT, min(MAX_HEIGHT, z))") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    # Set LED color if specified") |> ignore
    sb.AppendLine("    if color:") |> ignore
    sb.AppendLine("        set_led_color(cf, color[0], color[1], color[2])") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    # Go to position") |> ignore
    sb.AppendLine("    commander.go_to(x, y, z, 0, duration)") |> ignore
    sb.AppendLine("    time.sleep(duration)") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("def execute_formation(swarm, formation_name):") |> ignore
    sb.AppendLine("    \"\"\"") |> ignore
    sb.AppendLine("    Execute a single formation transition for all drones.") |> ignore
    sb.AppendLine("    \"\"\"") |> ignore
    sb.AppendLine("    if formation_name not in FORMATIONS:") |> ignore
    sb.AppendLine("        print(f'Unknown formation: {formation_name}')") |> ignore
    sb.AppendLine("        return") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    formation = FORMATIONS[formation_name]") |> ignore
    sb.AppendLine("    print(f'\\nExecuting formation: {formation_name}')") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    # Build args dict for parallel execution") |> ignore
    sb.AppendLine("    args = {}") |> ignore
    sb.AppendLine("    for drone_id, wp in formation['waypoints'].items():") |> ignore
    sb.AppendLine("        uri = DRONE_URIS[drone_id]") |> ignore
    sb.AppendLine("        color = wp.get('color', None)") |> ignore
    sb.AppendLine("        args[uri] = [wp['x'], wp['y'], wp['z'], wp['duration'], color]") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    # Execute in parallel") |> ignore
    sb.AppendLine("    swarm.parallel_safe(lambda scf, x, y, z, dur, col: go_to_position(scf, x, y, z, dur, col), args)") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    # Small pause between formations") |> ignore
    sb.AppendLine("    time.sleep(0.5)") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("") |> ignore
    
    // Main function
    sb.AppendLine("# ==============================================================================") |> ignore
    sb.AppendLine("# MAIN EXECUTION") |> ignore
    sb.AppendLine("# ==============================================================================") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("def run_show():") |> ignore
    sb.AppendLine("    \"\"\"Execute the complete drone show.\"\"\"") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    print('=' * 60)") |> ignore
    sb.AppendLine("    print('CRAZYFLIE SWARM CHOREOGRAPHY')") |> ignore
    sb.AppendLine($"    print('Generated by: {show.Metadata.GeneratedBy}')") |> ignore
    sb.AppendLine($"    print('Optimization: {show.Metadata.OptimizationMethod}')") |> ignore
    sb.AppendLine($"    print(f'Drones: {{len(DRONE_URIS)}}')") |> ignore
    sb.AppendLine("    print('=' * 60)") |> ignore
    sb.AppendLine("    print()") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    # Initialize drivers") |> ignore
    sb.AppendLine("    cflib.crtp.init_drivers()") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    factory = CachedCfFactory(rw_cache='./cf_cache')") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    with Swarm(DRONE_URIS, factory=factory) as swarm:") |> ignore
    sb.AppendLine("        print('Connected to all drones!')") |> ignore
    sb.AppendLine("        print()") |> ignore
    sb.AppendLine("        ") |> ignore
    sb.AppendLine("        # Reset estimators") |> ignore
    sb.AppendLine("        print('Resetting position estimators...')") |> ignore
    sb.AppendLine("        swarm.parallel_safe(reset_estimator)") |> ignore
    sb.AppendLine("        print()") |> ignore
    sb.AppendLine("        ") |> ignore
    sb.AppendLine("        # Takeoff") |> ignore
    sb.AppendLine("        print('Taking off...')") |> ignore
    sb.AppendLine("        swarm.parallel_safe(takeoff)") |> ignore
    sb.AppendLine("        print('All drones airborne!')") |> ignore
    sb.AppendLine("        print()") |> ignore
    sb.AppendLine("        ") |> ignore
    sb.AppendLine("        # Execute formation sequence") |> ignore
    sb.AppendLine("        try:") |> ignore
    sb.AppendLine("            for formation_name in FORMATION_SEQUENCE:") |> ignore
    sb.AppendLine("                if EMERGENCY_STOP.is_set():") |> ignore
    sb.AppendLine("                    print('Emergency stop triggered!')") |> ignore
    sb.AppendLine("                    break") |> ignore
    sb.AppendLine("                execute_formation(swarm, formation_name)") |> ignore
    sb.AppendLine("        except KeyboardInterrupt:") |> ignore
    sb.AppendLine("            print('\\nShow interrupted by user')") |> ignore
    sb.AppendLine("            EMERGENCY_STOP.set()") |> ignore
    sb.AppendLine("        ") |> ignore
    sb.AppendLine("        # Land") |> ignore
    sb.AppendLine("        print()") |> ignore
    sb.AppendLine("        print('Landing...')") |> ignore
    sb.AppendLine("        swarm.parallel_safe(land)") |> ignore
    sb.AppendLine("        print('All drones landed!')") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    print()") |> ignore
    sb.AppendLine("    print('Show complete!')") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("") |> ignore
    sb.AppendLine("if __name__ == '__main__':") |> ignore
    sb.AppendLine("    logging.basicConfig(level=logging.WARNING)") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    print()") |> ignore
    sb.AppendLine("    print('SAFETY CHECKLIST:')") |> ignore
    sb.AppendLine("    print('  1. Lighthouse/Loco positioning system is running')") |> ignore
    sb.AppendLine("    print('  2. All drones are on the ground in starting positions')") |> ignore
    sb.AppendLine("    print('  3. Flight area is clear of obstacles')") |> ignore
    sb.AppendLine("    print('  4. Emergency stop is accessible (Ctrl+C)')") |> ignore
    sb.AppendLine("    print()") |> ignore
    sb.AppendLine("    ") |> ignore
    sb.AppendLine("    response = input('Ready to start? (yes/no): ')") |> ignore
    sb.AppendLine("    if response.lower() in ['yes', 'y']:") |> ignore
    sb.AppendLine("        run_show()") |> ignore
    sb.AppendLine("    else:") |> ignore
    sb.AppendLine("        print('Show cancelled.')") |> ignore
    
    sb.ToString()

/// Write Python script to file
let writePythonScript (path: string) (show: ShowDefinition) =
    File.WriteAllText(path, toPythonScript show)
    printfn "Wrote Python script to: %s" path

// =============================================================================
// CONVERSION FROM TRANSITION RESULTS
// =============================================================================

/// Convert TransitionResult array to ShowDefinition
let fromTransitionResults 
    (transitions: {| FromFormation: string; ToFormation: string; Assignments: {| DroneId: int; TargetPositionIndex: int |}[]; TotalDistance: float; Method: string |}[])
    (formations: {| Name: string; Positions: {| X: float; Y: float; Z: float |}[] |}[])
    (scale: float)
    (transitionDuration: float)
    : ShowDefinition =
    
    let numDrones = 
        if formations.Length > 0 then formations.[0].Positions.Length
        else 4
    
    // Create drone configs
    let drones = 
        [| for i in 0 .. numDrones - 1 do
            {
                Id = i
                Uri = if i < defaultDroneUris.Length then defaultDroneUris.[i] else $"radio://0/80/2M/E7E7E7E7{i:X2}"
                Name = Some $"Drone {i}"
            }
        |]
    
    // Create formation waypoints
    let mutable timestampMs = 0
    let formationWaypoints = ResizeArray<FormationWaypoints>()
    
    // Add starting formation (before first transition)
    if formations.Length > 0 then
        let startFormation = formations.[0]
        formationWaypoints.Add({
            Name = $"Start ({startFormation.Name})"
            TimestampMs = 0
            Waypoints = 
                [| for i, pos in Array.indexed startFormation.Positions do
                    {
                        DroneId = i
                        Position = convertPosition pos scale
                        Color = if i < defaultColors.Length then Some defaultColors.[i] else None
                        Duration = transitionDuration
                    }
                |]
        })
        timestampMs <- int (transitionDuration * 1000.0)
    
    // Add each transition
    for t in transitions do
        timestampMs <- timestampMs + int (transitionDuration * 1000.0)
        
        // Find target formation
        let targetFormation = 
            formations |> Array.tryFind (fun f -> f.Name = t.ToFormation)
        
        match targetFormation with
        | Some tf ->
            formationWaypoints.Add({
                Name = t.ToFormation
                TimestampMs = timestampMs
                Waypoints =
                    [| for a in t.Assignments |> Array.sortBy (fun x -> x.DroneId) do
                        let targetPos = tf.Positions.[a.TargetPositionIndex]
                        {
                            DroneId = a.DroneId
                            Position = convertPosition targetPos scale
                            Color = if a.DroneId < defaultColors.Length then Some defaultColors.[a.DroneId] else None
                            Duration = transitionDuration
                        }
                    |]
            })
        | None -> ()
    
    let totalDistance = transitions |> Array.sumBy (fun t -> t.TotalDistance)
    let optimizationMethod = 
        let quantumCount = transitions |> Array.filter (fun t -> t.Method.Contains("Quantum")) |> Array.length
        if quantumCount > transitions.Length / 2 then "Quantum (QAOA)"
        elif quantumCount > 0 then "Hybrid (Quantum + Classical)"
        else "Classical (Greedy)"
    
    {
        Metadata = {
            GeneratedBy = "FSharp.Azure.Quantum SwarmChoreography"
            GeneratedAt = DateTimeOffset.UtcNow
            OptimizationMethod = optimizationMethod
            NumDrones = numDrones
            TotalTransitions = transitions.Length
            TotalDistanceMeters = totalDistance
        }
        Drones = drones
        Formations = formationWaypoints.ToArray()
    }
