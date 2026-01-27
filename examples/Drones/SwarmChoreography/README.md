# Drone Swarm Choreography

**Quantum-Optimized Formation Planning with Real Drone Export**

This example demonstrates quantum optimization using QAOA to solve the Quadratic Assignment Problem (QAP) for drone formation transitions. It uses 4 drones (16 qubits) which fits within the LocalBackend's 20-qubit limit, enabling actual quantum execution and **real drone automation** through Crazyflie or MAVLink export.

## Quantum Compliance

This example is **fully quantum compliant**:
- All optimization uses QAOA via `IQuantumBackend`
- Classical greedy is only used as internal fallback if quantum solver fails
- No standalone classical solver exposed in public API

## Key Features

- **16 qubits** (4 drones x 4 positions) - fits LocalBackend limit
- **Quantum solver executes** - actual QAOA optimization
- **Collision avoidance** - optional extension for safe path planning
- **Crazyflie export** - generates executable Python scripts for indoor micro-drones
- **MAVLink export** - generates mission files for ArduPilot/PX4 drones (outdoor)
- **JSON waypoints** - standard format for custom integrations

## Quick Start

```bash
# Build and run with quantum optimization
dotnet run -- --shots 2000

# Run with Crazyflie export (indoor micro-drones)
dotnet run -- --export

# Run with MAVLink export (outdoor ArduPilot/PX4 drones)
dotnet run -- --mavlink --home-lat 50.4501 --home-lon 30.5234

# Custom export parameters
dotnet run -- --export --scale 0.1 --duration 5.0

# Both exports simultaneously
dotnet run -- --export --mavlink --home-lat 50.4501 --home-lon 30.5234
```

## Command Line Options

| Option | Default | Description |
|--------|---------|-------------|
| `--shots` | `2000` | Number of quantum measurements |
| `--out` | `runs/drone/swarm` | Output directory |
| `--export` | (flag) | Generate Crazyflie Python script |
| `--mavlink` | (flag) | Generate MAVLink mission files |
| `--scale` | `0.05` | Position scale factor for indoor use |
| `--duration` | `3.0` | Transition duration in seconds |
| `--home-lat` | (required for MAVLink) | Home position latitude (decimal degrees) |
| `--home-lon` | (required for MAVLink) | Home position longitude (decimal degrees) |
| `--home-alt` | `0.0` | Home position altitude (meters) |

## Show Sequence

```
Ground (Line) → Diamond → Square → Vertical Line → Ground
```

### Formation Layouts

**Ground (Line)** - Starting/ending formation:
```
0   1   2   3    (drones at Z=0, spaced 4m apart)
```

**Diamond**:
```
      3         ← Top (Z=25m)
    1   2       ← Sides (Z=15m)
      0         ← Bottom (Z=5m)
```

**Square** (2x2 grid):
```
0   1    ← Top (Z=20m)
2   3    ← Bottom (Z=10m)
```

**Vertical Line**:
```
  2   ← Top (Z=30m)
  1
  0
  3   ← Bottom (Z=6m)
```

## Crazyflie Export

The `--export` flag generates two files:

### 1. `crazyflie_show.json` - Waypoint Data

```json
{
  "metadata": {
    "generated_by": "FSharp.Azure.Quantum SwarmChoreography",
    "optimization_method": "Quantum (QAOA)",
    "num_drones": 4,
    "total_distance_meters": 205.06
  },
  "drones": [
    { "id": 0, "uri": "radio://0/80/2M/E7E7E7E700" }
  ],
  "formations": [
    {
      "name": "Diamond",
      "timestamp_ms": 6000,
      "waypoints": [
        { "drone_id": 0, "x": 0.0, "y": 0.0, "z": 0.25, "duration": 3.0, "color": {"r": 255, "g": 0, "b": 0} }
      ]
    }
  ]
}
```

### 2. `crazyflie_show.py` - Executable Script

Complete Python script using `cflib` that:
- Connects to Crazyflie drones via Crazyradio
- Waits for position estimation (Lighthouse/Loco)
- Executes synchronized takeoff
- Flies all formations in sequence with LED colors
- Handles emergency stop (Ctrl+C)
- Lands all drones safely

## Running on Real Drones

### Hardware Requirements

| Component | Recommended | Purpose |
|-----------|-------------|---------|
| **Drones** | Crazyflie 2.1 x 4 | 27g micro quadcopters |
| **Radio** | Crazyradio PA | USB radio dongle |
| **Positioning** | Lighthouse 2.0 | mm-precision indoor tracking |
| **Computer** | Any with USB | Running Python script |

### Setup Steps

1. **Install Crazyflie library**:
   ```bash
   pip install cflib
   ```

2. **Configure Lighthouse positioning**:
   - Mount 2 Lighthouse base stations
   - Flash Lighthouse deck firmware
   - Calibrate using Crazyflie Client

3. **Update drone URIs** in generated Python script:
   ```python
   DRONE_URIS = [
       'radio://0/80/2M/E7E7E7E700',  # Your drone 0 address
       'radio://0/80/2M/E7E7E7E701',  # Your drone 1 address
       # ...
   ]
   ```

4. **Adjust safety parameters**:
   ```python
   MAX_HEIGHT = 2.0  # Adjust for your ceiling height
   MIN_HEIGHT = 0.2  # Minimum safe altitude
   ```

5. **Run the show**:
   ```bash
   python crazyflie_show.py
   ```

## MAVLink Export

The `--mavlink` flag generates mission files compatible with ArduPilot and PX4 flight controllers, enabling outdoor drone swarm shows with standard drones (DJI, Pixhawk-based, etc.).

**Required parameters:**
- `--home-lat` - Home position latitude (decimal degrees)
- `--home-lon` - Home position longitude (decimal degrees)
- `--home-alt` (optional) - Home altitude in meters (default: 0.0)

### Generated Files

The MAVLink export creates three files:

#### 1. `mavlink_mission.plan` - QGroundControl Plan

JSON format that loads directly into QGroundControl:

```json
{
  "fileType": "Plan",
  "version": 1,
  "groundStation": "QGroundControl",
  "mission": {
    "plannedHomePosition": [50.4501, 30.5234, 0.0],
    "items": [
      {
        "command": 22,
        "params": [0, 0, 0, 0, 50.4501, 30.5234, 5.0]
      }
    ]
  }
}
```

#### 2. `mavlink_mission.waypoints` - MAVLink Waypoint Format

Standard MAVLink waypoint format compatible with Mission Planner and ArduPilot:

```
QGC WPL 110
0	1	0	16	0	0	0	0	50.450100	30.523400	0.000000	1
1	0	3	22	0	0	0	0	50.450100	30.523400	5.000000	1
2	0	3	16	0	0	0	0	50.450108	30.523392	15.000000	1
```

#### 3. `mavlink_swarm.py` - Python Control Script

Complete Python script using pymavlink/dronekit that:
- Connects to multiple drones via UDP/serial
- Arms drones and sets flight mode
- Executes synchronized takeoff to safe altitude
- Flies all formations in sequence with position commands
- Supports both ArduPilot and PX4 protocols
- Handles emergency stop (Ctrl+C)
- Lands all drones safely

### MAVLink Hardware Requirements

| Component | Required? | Recommended | Purpose |
|-----------|-----------|-------------|---------|
| **Flight Controller** | Yes | Pixhawk 4/6, Cube Orange | ArduPilot/PX4 compatible |
| **GPS** | Yes | u-blox M8N/M9N | Outdoor positioning |
| **Ground Station** | Yes | QGroundControl | Mission upload & monitoring |
| **Telemetry Radio** | No* | SiK Radio 915MHz | Real-time communication |
| **RTK GPS** | No | u-blox F9P | cm-level precision (for tight formations) |
| **4G/LTE Modem** | No | Holybro 4G Module | Long-range telemetry alternative |

*For testing, you can use USB connection or WiFi. Telemetry radio recommended for actual outdoor flights.

### MAVLink Setup Steps

1. **Install dependencies**:
   ```bash
   pip install pymavlink dronekit
   ```

2. **Configure drones**:
   - Flash ArduPilot or PX4 firmware
   - Calibrate sensors (accelerometer, compass, gyro)
   - Configure GPS and wait for 3D fix
   - Set flight modes (GUIDED mode required)

3. **Update connection strings** in `mavlink_swarm.py`:
   ```python
   DRONE_CONNECTIONS = [
       'udp:127.0.0.1:14550',  # Drone 0 (SITL or telemetry)
       'udp:127.0.0.1:14560',  # Drone 1
       'udp:127.0.0.1:14570',  # Drone 2
       'udp:127.0.0.1:14580',  # Drone 3
   ]
   ```

4. **For real hardware**, use serial or UDP connections:
   ```python
   DRONE_CONNECTIONS = [
       '/dev/ttyUSB0',           # Serial telemetry radio
       'udpin:0.0.0.0:14550',    # UDP from telemetry
       'tcp:192.168.1.10:5760',  # TCP to companion computer
   ]
   ```

5. **Upload QGC plan** (alternative to Python script):
   - Open QGroundControl
   - Connect to drone
   - Plan → Load Plan → select `mavlink_mission.plan`
   - Upload to vehicle

6. **Run the swarm show**:
   ```bash
   python mavlink_swarm.py
   ```

### MAVLink Safety Checklist

Before running outdoor swarm operations:
- [ ] All drones have valid GPS lock (>8 satellites, HDOP < 2.0)
- [ ] Flight area is clear of people and obstacles
- [ ] Airspace authorization obtained (if required)
- [ ] Battery levels checked (>80% for full show)
- [ ] Telemetry links verified for all drones
- [ ] Emergency stop accessible (Ctrl+C or RC failsafe)
- [ ] Wind conditions acceptable (<15 km/h recommended)
- [ ] Home position set correctly (--home-lat, --home-lon)
- [ ] Geofence configured in flight controller
- [ ] Return-to-Launch (RTL) altitude set appropriately

### Safety Checklist

Before running:
- [ ] Lighthouse/Loco system running and calibrated
- [ ] All drones on ground at starting positions
- [ ] Flight area clear of obstacles and people
- [ ] Battery levels checked (>50%)
- [ ] Emergency stop accessible (Ctrl+C)

## Scale Factor

The `--scale` parameter converts outdoor show dimensions to indoor-safe dimensions:

| Original (m) | Scale 0.05 | Scale 0.1 |
|--------------|------------|-----------|
| 30m altitude | 1.5m | 3.0m |
| 16m horizontal | 0.8m | 1.6m |
| 4m spacing | 0.2m | 0.4m |

**Recommended scales**:
- Small room (3x3m): `--scale 0.03`
- Medium room (5x5m): `--scale 0.05` (default)
- Large room (10x10m): `--scale 0.1`
- Outdoor: `--scale 1.0`

## Example Output

```
╔══════════════════════════════════════════════════╗
║  SHOW SUMMARY                                    ║
╠══════════════════════════════════════════════════╣
║  Drones: 4 (16 qubits)                           ║
║  Transitions: 4                                  ║
║  Total Flight Distance:   205.06 meters          ║
║  Elapsed Time: 7841 ms                           ║
╠══════════════════════════════════════════════════╣
║  Quantum compliant: Quantum solver via IBackend  ║
║  Quantum solved: 4 | Fallback used: 0            ║
╚══════════════════════════════════════════════════╝

╔══════════════════════════════════════════════════╗
║  CRAZYFLIE EXPORT                                ║
╚══════════════════════════════════════════════════╝
Wrote JSON waypoints to: runs/drone/swarm/crazyflie_show.json
Wrote Python script to: runs/drone/swarm/crazyflie_show.py
```

## Files Generated

| File | Flag | Description |
|------|------|-------------|
| `metrics.json` | (always) | Performance metrics (JSON) |
| `run-report.md` | (always) | Human-readable summary |
| `crazyflie_show.json` | `--export` | Waypoint data for custom integrations |
| `crazyflie_show.py` | `--export` | Executable Crazyflie Python script |
| `mavlink_mission.plan` | `--mavlink` | QGroundControl mission plan |
| `mavlink_mission.waypoints` | `--mavlink` | MAVLink waypoint format |
| `mavlink_swarm.py` | `--mavlink` | Python control script (pymavlink/dronekit) |

## Quantum Computing Notes

### Quantum Architecture

```fsharp
let solve (backend: IQuantumBackend) (shots: int) (distanceMatrix: float[,]) 
    : Result<Assignment[], string> =
    
    // 1. Build QUBO from distance matrix
    let qubo = QapQubo.buildQubo distanceMatrix penaltyWeight
    
    // 2. Convert to problem Hamiltonian
    let problemHam = ProblemHamiltonian.fromQubo qubo
    let mixerHam = MixerHamiltonian.create numVars
    
    // 3. Build and execute QAOA circuit via backend
    let circuit = QaoaCircuit.build problemHam mixerHam parameters
    match backend.ExecuteToState circuit with
    | Ok state -> 
        // Sample and decode
        let measurements = QuantumState.measure state shots
        decodeBestSolution measurements
    | Error err -> 
        // Internal classical fallback (private)
        Ok (classicalGreedy distanceMatrix)
```

### QUBO Encoding

For n drones and n positions:
- Variables: `x[i,j] = 1` if drone i assigned to position j
- Total variables: n² = 16 for 4 drones
- Qubits required: 16 (within 20-qubit limit)

## Collision Avoidance Extension

The `CollisionAvoidance` module is an **optional extension** that ensures drones don't collide during formation transitions. The main solver optimizes **which drone goes where**, but doesn't consider **when** each drone moves. With simultaneous straight-line paths, drones might collide mid-flight.

### The Problem

Consider transitioning from Diamond to Square formation:
```
Diamond:          Square:
      0               0   1
    1   2       →   
      3               2   3
```

If drone 0 (top) goes to bottom-left and drone 3 (bottom) goes to top-right, their paths cross in the middle.

### The Solution

The collision avoidance module uses QAOA to find **optimal timing offsets** - each drone can have a delay before starting its transition. This is formulated as another QUBO problem:

- Variables: `x[d,k] = 1` if drone d has delay step k
- For 4 drones with 4 delay options: 16 qubits (fits LocalBackend)
- Constraints: One delay per drone, minimize collision risk

### Usage

```fsharp
open FSharp.Azure.Quantum.Examples.Drones.SwarmChoreography.CollisionAvoidance

// After getting assignments from main solver
let assignments = [(0, 2); (1, 0); (2, 3); (3, 1)]

// Check for collision risks (no quantum execution)
let risk = validateTransition currentPositions targetPositions assignments PlanningConstraints.defaults
printfn "%s" (describeRisk risk)

// Or get a collision-free plan (uses QAOA if needed)
let constraints = 
    PlanningConstraints.defaults
    |> PlanningConstraints.withSeparation 1.5  // 1.5m minimum
    |> PlanningConstraints.withDelaySteps 4    // 4 timing options

match planTransition backend 1000 currentPositions targetFormation assignments constraints with
| Ok plan ->
    printfn "Method: %s" plan.Method
    printfn "Min separation: %.2fm" plan.MinAchievedSeparation
    printfn "Max delay: %.2f (normalized)" plan.MaxDelay
    for path in plan.Paths do
        printfn "Drone %d: starts at t=%.2f" path.DroneId path.Waypoints.[0].DwellTime
| Error msg ->
    printfn "Planning failed: %s" msg
```

### Constraints Builder

```fsharp
// Fluent configuration
let constraints = 
    PlanningConstraints.defaults           // 2.0m separation, 4 delay steps
    |> PlanningConstraints.withSeparation 1.5
    |> PlanningConstraints.withDelaySteps 6
    |> PlanningConstraints.withMaxVelocity 3.0
    |> PlanningConstraints.withSamples 30
```

### Qubit Scaling

| Drones | Delay Steps | Qubits | Fits LocalBackend? |
|--------|-------------|--------|-------------------|
| 4 | 4 | 16 | Yes |
| 4 | 5 | 20 | Yes (limit) |
| 5 | 4 | 20 | Yes (limit) |
| 8 | 4 | 32 | No (needs Azure) |
| 100 | 4 | 400 | No (needs Azure) |

### When to Use

- **Indoor shows**: Tight spaces increase collision risk
- **Dense formations**: Small separation between positions
- **Crossing transitions**: Assignments that swap drone positions
- **Safety-critical**: When any collision must be prevented

### Behavior

1. **No collisions detected**: Returns direct paths immediately (no QAOA needed)
2. **Collisions detected**: Uses QAOA to find safe timing offsets
3. **Quantum fails**: Falls back to greedy sequential timing

### Output

The plan includes timing information for each drone:

```fsharp
type CollisionFreePlan = {
    Paths: DronePath list           // Path with timing for each drone
    OriginalAssignments: (int * int) list
    TotalDistance: float            // Same as without collision avoidance
    MinAchievedSeparation: float    // Actual minimum separation achieved
    MaxDelay: float                 // Maximum delay used (0-1 normalized)
    Method: string                  // "Direct" or "Quantum (QAOA)"
}
```

## Dynamic Behavior Extension

The `DynamicBehavior` module enables **drone-initiated events** and **real-time swarm adaptation**. Drones can self-initiate events (low battery, point of interest detected, etc.) and the swarm adapts formations dynamically.

### Concept

```
Mission Timeline:
Formation A ──────► Formation B ──────► Formation C
                         │
                    ┌────┴────┐
                    │ EVENT:  │
                    │ Drone 2 │
                    │ battery │
                    │ low     │
                    └────┬────┘
                         │
            ┌────────────┴────────────┐
            │ SHORT EVENT (<30s):     │ LONG EVENT:
            │ Others HOLD, wait       │ Others CONTINUE
            │ Drone rejoins           │ Recalculate for N-1
            └────────────┬────────────┘
                         │
                    Continue show
```

### Drone Profiles

Different drone types have different thresholds:

```fsharp
open FSharp.Azure.Quantum.Examples.Drones.SwarmChoreography.DynamicBehavior

// Pre-defined profiles
let profile = DroneProfile.crazyflie      // Indoor micro-drone
let profile = DroneProfile.standard       // Outdoor quad
let profile = DroneProfile.heavyLifter    // Cargo drone (higher battery threshold)
let profile = DroneProfile.scout          // Recon drone (lower threshold, optimized)
```

### Standard Events

Drones can self-initiate these events based on internal state:

| Category | Events |
|----------|--------|
| **Battery** | `LowBattery`, `CriticalBattery`, `RechargeComplete` |
| **Navigation** | `ObstacleDetected`, `GpsLost`, `GpsRecovered`, `ReturnToHome` |
| **Mission** | `PointOfInterest`, `ItemReadyToDrop`, `ItemDropped`, `PayloadPickedUp` |
| **Social** | `PersonRecognized`, `GestureDetected`, `FollowMeRequested` |
| **Environmental** | `HighWind`, `TemperatureWarning`, `RainDetected` |
| **Hardware** | `MotorWarning`, `SensorFault`, `CommunicationDegraded` |
| **Formation** | `ReadyToRejoin`, `FormationPositionReached`, `CollisionRisk` |

### Communication Protocol

Simple text-based protocol works with both MAVLink STATUSTEXT and Crazyflie console:

```
# Notification from drone
EVT|2|BAT_LOW|X|10.5|5.2|15.0|18

# Command to drone(s)
CMD|0,1,3|HOLD|30
CMD|*|RESUME|
```

### Usage

```fsharp
open FSharp.Azure.Quantum.Examples.Drones.SwarmChoreography.DynamicBehavior

// Create swarm state
let profiles = [DroneProfile.standard; DroneProfile.standard; DroneProfile.heavyLifter; DroneProfile.scout]
let state = SwarmState.create 4 profiles

// Receive notification from drone
let notification = {
    DroneId = 2
    Event = Standard (LowBattery 18.0)
    CurrentPosition = Position.create 10.5 5.2 15.0
    Timestamp = DateTime.UtcNow
    Priority = Normal
}

// Handle event - swarm adapts
let newState, commands = handleNotification backend config state notification

// Commands tell other drones what to do
for cmd in commands do
    printfn "Send: %s" (Protocol.encodeCommand cmd)
```

### Event Duration

Events are classified by expected duration:

| Duration | Behavior | Example |
|----------|----------|---------|
| `Momentary` (<10s) | Others hover, wait for rejoin | `ItemDropped` |
| `Brief` (10-60s) | Others hover or slow-loop | `PointOfInterest` |
| `Extended` (unknown) | Recalculate formations for N-1 drones | `LowBattery`, `ReturnToHome` |

### Custom Events

For domain-specific needs:

```fsharp
let customEvent = Custom {
    EventType = "THERMAL_DETECTED"
    Payload = Map.ofList ["lat", "50.45"; "lon", "30.52"; "strength", "0.8"]
    SuggestedDuration = Brief 20.0
}
```

## See Also

- [FleetPathPlanning](../FleetPathPlanning/README.md) - TSP for drone delivery routes
- [SwarmTaskAllocation](../SwarmTaskAllocation/README.md) - Job shop scheduling for drone tasks

## References

### Crazyflie (Indoor Micro-drones)
1. **Crazyflie Documentation** - https://www.bitcraze.io/documentation/
2. **Lighthouse Positioning** - https://www.bitcraze.io/documentation/system/positioning/lighthouse/
3. **cflib API** - https://www.bitcraze.io/documentation/repository/crazyflie-lib-python/

### MAVLink (Outdoor Drones)
4. **MAVLink Protocol** - https://mavlink.io/en/
5. **ArduPilot Documentation** - https://ardupilot.org/copter/
6. **PX4 User Guide** - https://docs.px4.io/main/en/
7. **QGroundControl** - https://docs.qgroundcontrol.com/master/en/
8. **pymavlink** - https://github.com/ArduPilot/pymavlink
9. **dronekit-python** - https://dronekit-python.readthedocs.io/

### Quantum Computing
10. **QAOA** - Farhi et al. (2014) - arXiv:1411.4028
