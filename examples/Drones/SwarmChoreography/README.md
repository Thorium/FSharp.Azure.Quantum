# Drone Swarm Choreography

**Quantum-Optimized Formation Planning with Real Drone Export**

This example demonstrates quantum optimization using QAOA to solve the Quadratic Assignment Problem (QAP) for drone formation transitions. It uses 4 drones (16 qubits) which fits within the LocalBackend's 20-qubit limit, enabling actual quantum execution and **real drone automation** through Crazyflie or MAVLink export.

## Quantum Compliance

This example is **fully quantum compliant**:
- All optimization uses QAOA via `IQuantumBackend`
- Classical greedy is only used as internal fallback if quantum solver fails
- A transition whose QAOA assignment would bring two drones too close in flight is replaced by the minimum-squared-distance assignment (the safety gate, see MAVLink Export)
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
| `--scale` | automatic | Position scale factor for both exports. Without it, `--mavlink` picks the scale that puts the closest slots and the closest synchronised transit at 1.2 × the 5 m minimum separation (1.81 for this show, measured on the S-curve legs as flown), and the Crazyflie show scales each formation for the room (see Indoor layout); both are printed |
| `--duration` | `3.0` | Transition duration in seconds |
| `--home-lat` | (required for MAVLink) | Home position latitude (decimal degrees) |
| `--home-lon` | (required for MAVLink) | Home position longitude (decimal degrees) |
| `--home-alt` | `0.0` | Site altitude above mean sea level (metres); written only where the formats are absolute (plannedHomePosition, `.waypoints` row 0) |
| `--pilots` | `1` | Remote pilots asked for in the 1:N permission evidence |
| `--endurance-min` | `20` (MAVLink) / `7` (Crazyflie) | Nominal endurance used by the evidence pack (an assumption: no figure exists for these aircraft) |
| `--room-x` / `--room-y` | `4` / `4` | Indoor room size in metres (east-west / north-south, centred on the origin); every point of the Crazyflie show must stay 0.5 m from each wall |

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
- Refuses a show with a waypoint outside `MIN_HEIGHT`-`MAX_HEIGHT` (nothing is clamped in flight)
- **Uploads each drone's whole show as an on-board trajectory** (`Poly4D` pieces: a smooth 7th-order move and a 0.5 s pause per formation) before take-off, takes off, and starts all trajectories. A drone that loses the radio after the start keeps flying its show and then holds above its final slot. Verify on your firmware that a started high-level-commander trajectory keeps running, and the last setpoint is held, without radio traffic.
- Handles emergency stop (Ctrl+C, also during take-off): every drone lands where it is; the landing is in a `finally`, so any way out of the show lands every drone
- Lands all drones on their own start slots

### Indoor layout

The show is drawn in a vertical plane, but a room has a 2 m ceiling, and indoors the emergency action is to land every drone where it is. Stacked slots cannot survive that: the Diamond's top would land on its bottom. The Vertical Line would also need about 15 m of height to be separated at all. So the export lays the show out for the room:

- **Every airborne formation is rotated into the horizontal plane** (the picture is seen from above) and flown at 1.8 m, 0.2 m under `MAX_HEIGHT`. The ground formations are flown at the 0.5 m take-off height.
- **Each formation is scaled on its own** (unless `--scale` is given) to the smallest size that keeps its closest slots, and every synchronised transit, at 1.2 × the indoor limit (0.6). This show uses 0.212 / 0.066 / 0.085 / 0.106 / 0.212.
- **The optimiser's assignments are re-checked by the safety gate in this layout.** An assignment that is safe in the drawn plane need not be safe once the formations are flat and sized differently.
- **The show closes on each drone's own start slot.** Every drone flies at show height to above its slot, then all descend together. A drone that never got past take-off (lost radio) or that dropped out is on its own slot, and nobody else lands there.
- `--room-x` / `--room-y` (default 4 × 4 m, centred on the origin) bound the show. The evidence pack checks every point is at least 0.5 m from each wall.

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
- `--home-lat` - Show origin latitude (decimal degrees)
- `--home-lon` - Show origin longitude (decimal degrees)
- `--home-alt` (optional) - Site altitude above mean sea level in meters (default: 0.0)

### What each drone's mission does

- **Arms on its own ground slot** (around the show origin), which the files declare as that drone's home (`plannedHomePosition`, `.waypoints` row 0). ArduPilot sets home where the vehicle arms, so RTL brings each drone back to its own slot, not to one point shared by the swarm.
- **Takes off** vertically to 2 m, then flies only the **airborne** formations (Diamond, Square, Vertical Line). The ground formations are where the drones stand. They are never flown to at 0 m mid-show.
- **Flies every transition in step with the others.** Before each leg a `DO_CHANGE_SPEED` sets the drone's horizontal speed so its leg takes as long as the slowest drone's leg at 3 m/s.
  - The leg time counts the climb and descent limits (`WPNAV_SPEED_UP` 2.5 m/s, `WPNAV_SPEED_DN` 1.5 m/s).
  - It also counts **ArduPilot's S-curve acceleration**: every leg starts and ends at rest, jerk- and acceleration-limited by `WPNAV_ACCEL` 2.5 m/s², `WPNAV_ACCEL_Z` 1 m/s², `WPNAV_JERK` 1 m/s³ and `PSC_JERK_Z` 5 m/s³. These are pinned in the `.parm` files, and the model is a symmetric approximation of ArduPilot's SCurve.
  - A leg too short to fly that slowly is flown at 0.5 m/s and the drone holds longer, so all drones leave each formation together.
  - **Holds are exported as whole seconds** because ArduPilot keeps a waypoint's `param1` as an integer (`AP_Mission`, `cmd.p1` is a `uint16`). The fraction follows as a **`NAV_DELAY`**, whose seconds are a float. A fractional hold used to be silently truncated.
- **Lands on its own slot** (`NAV_LAND` with a position: it flies there at its current height, then descends).
- Altitudes are **relative to home** (`MAV_FRAME_GLOBAL_RELATIVE_ALT`, QGC AltitudeMode 1). `--home-alt` used to be added to them, which put every waypoint `--home-alt` metres too high.

The optimiser's assignments pass a **transition safety gate**: an assignment whose synchronised straight lines bring two drones closer than min(start spacing, end spacing)/√2 is replaced by the assignment that minimises the sum of squared distances. That assignment meets the bound by construction (Turpin, Michael & Kumar, "CAPT", 2014). The QAOA sample often misses it, so the gate typically replaces 1-2 of the 4 transitions, labelled `Classical (safety fallback)`.

### Generated Files

The MAVLink export creates these files:

#### 1. `<DroneName>_mission.plan` (one per drone, e.g. `Drone0_mission.plan`) - QGroundControl Plan

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

#### 3. `<DroneName>.parm` - ArduPilot Parameters (load before flight)

One `NAME VALUE` line per parameter (MAVProxy `param load` format). The mission and its failsafes only behave as planned with these values, and the evidence pack reads them back from disk:

- `RTL_ALT`: **staggered per drone**, all above the show's top point and 6 m apart (e.g. 61 / 67 / 73 / 79 m), so drones returning at once cross at different heights. A drone that is ever stacked under another gets the lower RTL altitude, because RTL first climbs vertically.
- `RTL_CONE_SLOPE 0`, `RTL_ALT_FINAL 0`, `RTL_SPEED`, and `RTL_LOIT_TIME`. The loiter is long enough that no drone starts its descent before every drone has finished its RTL climb and transit (31 s here).
- `WPNAV_SPEED_UP/DN`, `LAND_SPEED`, `LAND_ALT_LOW`: the climb, descent and landing speeds the timing assumes. `WPNAV_ACCEL`, `WPNAV_ACCEL_Z`, `WPNAV_JERK` and `PSC_JERK_Z` are the S-curve limits it assumes.
- `FS_OPTIONS 11`: a lost RC or ground-station link in AUTO **continues the mission**, which is the deconflicted show, instead of starting a lone RTL. `BATT_FS_LOW_ACT 0`: low battery only warns, and the pilot runs the drop-out procedure (below).

#### 4. `mavlink_show.fsx` - F# Show Script

An F# script that flies the exported show over MAVLink 2. It uses the NuGet
package [`MAVLink`](https://www.nuget.org/packages/MAVLink) 1.0.8, which is
ArduPilot Mission Planner's generated C# MAVLink
([source](https://github.com/ArduPilot/MissionPlanner/tree/master/ExtLibs/Mavlink)).
It needs only the .NET SDK. The drones and their connections are the table at
the top of the script:
- `tcp:HOST:PORT`: SITL instance *i* serves `tcp:127.0.0.1:(5760 + 10 i)`, which is the default.
- `udpin:PORT`: a vehicle or MAVProxy `--out` sending to that port.

For each drone it:
1. Waits for the heartbeat.
2. Sets every parameter from `<Drone>.parm` and reads each one back.
3. Uploads `<Drone>_mission.plan` (home at seq 0, the items from seq 1) and reads the whole mission back.
4. Checks the vehicle stands within 2 m of its declared home, its own slot.

**It refuses to start on any difference**, so the vehicles carry exactly what
the evidence pack checked. Then it:
- sets GUIDED and arms every drone;
- sends `MISSION_START` to all at the same moment;
- streams position and mission progress.

Ctrl+C sends every drone RTL (the modelled abort).

```bash
cd runs/drone/swarm/mavlink
dotnet fsi mavlink_show.fsx --dry-run   # builds every message from the files, prints it, sends nothing
dotnet fsi mavlink_show.fsx             # connects and flies
```

The dry run also prints a `sim_vehicle.py --custom-location` line per drone,
which starts SITL on that drone's home.

**It has been dry-run only.** Run it against ArduPilot SITL (four instances,
each started on its drone's home) before any real aircraft.

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

1. **Install the .NET SDK** (the script restores its MAVLink package itself).

2. **Configure drones**:
   - Flash ArduPilot Copter firmware
   - Calibrate sensors (accelerometer, compass, gyro)
   - Configure GPS and wait for 3D fix
   - Place each drone on its own ground slot (its declared home; the dry run prints them)

3. **Set the connections** in the table at the top of `mavlink_show.fsx`:
   ```fsharp
   let drones =
       [
           ("Drone0", "tcp:127.0.0.1:5760")   // SITL instance 0, or a telemetry bridge
           ("Drone1", "udpin:14560")          // a vehicle / MAVProxy sending to UDP 14560
       ]
   ```

4. **Dry-run, then try it in SITL**, then fly:
   ```bash
   dotnet fsi mavlink_show.fsx --dry-run
   dotnet fsi mavlink_show.fsx
   ```

   The QGroundControl `.plan` files still load into QGroundControl for inspection,
   but only the script checks that every vehicle carries exactly the checked
   mission and parameters before anything flies.

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

**Recommended**: leave `--scale` out for both exports.
- Indoor (Crazyflie): each formation is scaled automatically for the room (see Indoor layout); give `--room-x` / `--room-y`. A uniform `--scale 0.05` puts drones 0.2 m apart and the evidence pack fails it.
- Outdoor (`--mavlink`): chosen automatically from the separation limit, measured on the S-curve legs as flown (1.81 for this show).

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
| `<DroneName>_mission.plan` (one per drone) | `--mavlink` | QGroundControl mission plan |
| `<DroneName>.waypoints` (one per drone) | `--mavlink` | MAVLink waypoint format |
| `<DroneName>.parm` (one per drone) | `--mavlink` | ArduPilot parameters (RTL altitude, failsafes, speeds) |
| `mavlink_show.fsx` | `--mavlink` | F# show script: uploads, verifies and flies the missions (`--dry-run` first) |
| `permission-evidence.md` / `.json` | (always) | 1:N (one pilot, many drones) permission evidence pack |

## 1:N Permission Evidence Pack

By default regulation expects one remote pilot per aircraft. Flying all four
drones under one pilot needs a 1:N permission, and the operator has to show how
the aircraft stay deconflicted, in range (battery with reserve, C2 link), under
the altitude ceiling, and what happens when one drops out. Every run writes that
evidence as `permission-evidence.md` and `.json` (module `Evidence` in
`Program.fs`, over the shared `Drones/_common/PermissionEvidence.fs`).

The pack checks **what is actually flown**. With `--mavlink` it reads the
exported mission items back item by item (take-off, the synchronised legs with
their `DO_CHANGE_SPEED`s and holds, the landing on each drone's own slot) and
reads every `<DroneName>.parm` back from disk. Otherwise it checks the
Crazyflie show as `crazyflie_show.py` flies it: take-off, the on-board
trajectory built from the same waypoints, then landing. It never rescales
anything to make a check pass.

**Indoors there is no airspace, so no 1:N permission is needed.** The pack is
still produced, framed as the collision-safety case a venue or operator needs
(the Operation line says so), with the same verdict logic. The indoor limit is
0.5 m beside another drone or 1.0 m straight below it: distance is measured as
sqrt(dx² + dy² + (0.5 dz)²) ≥ 0.5 m.
- **Horizontal 0.5 m:** a Crazyflie 2.1 is about 0.13 m across its propellers, and each drone's position is good to roughly 0.1 m (Lighthouse better, Loco worse). That gives 0.33 m, rounded up to 0.5 m.
- **Vertical 1.0 m:** hover thrust of 0.27 N through about 0.0064 m² of rotor disc drives a downwash near 4 m/s, which can upset a 27 g drone several rotor diameters below.
- This replaces the generic 2 m planning default of `CollisionAvoidance.PlanningConstraints`, which no 4-drone show fits in a room. It is a physical assumption, not an external standard.

| Area | What is checked |
|------|-----------------|
| Deconfliction | every drone has its own slot; outdoors a scale exists that separates the slots under the ceiling, indoors the room layout keeps slots and transits at 1.2 × the limit; exact closest approach of the rebuilt per-drone tracks vs. 5 m outdoors (`Safety.minSwarmSeparationMeters`) or the downwash-weighted 0.5 m indoors |
| Endurance | each drone's flight time vs. `--endurance-min` minus the 15% reserve |
| C2 link | horizontal distance from the pilot station (show origin) vs. the 2.4 GHz link budget |
| Altitude | outdoors, highest point of the show and of every contingency vs. the ceiling; indoors, every point inside the room (`--room-x` / `--room-y`, 0.5 m from each wall) and under `MAX_HEIGHT` |
| Contingency | each drone dropping out at each formation, judged on the worse of the show flying on unchanged (what both exports do: neither re-plans in flight) and a re-plan (others re-assigned by `DynamicBehavior.SwarmAdaptation.adaptFormation` through the same safety gate; indoors they hold while it descends, and it flies home under the show to its own slot); all drones stopping at once (outdoors RTL, triggered every second, plus the `.parm` files read back against the modelled failsafes; indoors emergency stop, landing all in place); indoors, each drone losing the radio during take-off (holds where it is) and after the start (flies its on-board show, then holds above its final slot) while the others fly on |
| Pilot ratio | `--pilots` vs. fleet and peak airborne (declared) |
| Supervisor workload | Each drop-out, the everyone-at-once abort, and a lost link are scenarios. A lost link is automatic: outdoors FS_OPTIONS continues the mission, indoors the on-board trajectory flies on. A drop-out and the abort are pilot decisions, so this show is supervised, not hands-off: 13 of 14 scenarios involve the pilot outdoors, 25 of 26 indoors, never more than one decision at once. |

**Drop-out procedure (outdoor).** A drone that has to leave the show steps 6 m
north out of the formation plane at its height and lands there, on clear ground
north of the show line. All formations lie in the east-up plane, so no one is
above or below it on the way down. A lone RTL is not used for this: ArduPilot's
RTL first climbs straight up, through any drone stacked above, and the pack
measured that as a collision at every scale tried (1.5 to 3.0). That is why
link loss continues the mission (`FS_OPTIONS`) and low battery only warns.

**Results** (`--mavlink --home-lat 61.5 --home-lon 23.8`, 5 runs): **PASS**
every time, all with the S-curve timing and whole-second holds modelled.
- Automatic scale 1.81; top of show 54.4 m.
- Closest approach in the show 6.1 m.
- Every single-drone drop-out at least 6.0 m, judged on the worse of "show unchanged" (what the exported missions fly) and "re-planned".
- All drones RTL at once: 6.00 m.
- RTL altitudes 61-79 m; all four `.parm` files as modelled.

**Indoor results** (default run, 4 × 4 m room, 9 runs): **PASS** every time.
- Closest slots 0.85 and closest transit 0.60 (weighted).
- Closest approach in the show 0.6 m.
- Every drop-out ≥ 0.56 m; emergency stop 0.60 m.
- Lost radio: worst 0.56 m, a drone left hovering at take-off height while a neighbour climbs out.
- Every point stays inside the room.

Before these changes the indoor show failed. The slots were 0.2 m apart and the Vertical Line needed 15 m of height. Every drop-out and the emergency stop landed drones on each other, and lost radio was NOT EVIDENCED.

**Crazyflie fixes made from this evidence:**
1. Airborne formations are laid out flat at 1.8 m, and each formation is scaled for the room.
2. Transitions are re-gated in the indoor layout.
3. The show closes on each drone's own start slot, via an overhead formation.
4. Each drone's show is uploaded as an on-board trajectory, so a lost radio does not stop it among the others.
5. Drop-out procedure: the others hold, the drone descends below the show and lands on its own slot.

**MAVLink fixes made from this evidence:**
1. Missions follow the optimiser's assignments (`MAVLinkExport.assignedFormations`).
   They used to send drone *i* to slot *i* of every formation.
2. Waypoint altitudes are relative to home. `--home-alt` was added to them.
3. The scale is automatic outdoors (0.05 was an indoor number: drones centimetres apart).
4. The transitions are time-synchronised. Before this, drones with short legs arrived early and drifted out of the straight-line model.
5. There are no mid-show visits to 0 m. Each drone lands on its own slot, with no RTL onto a shared home.
6. Each drone has its own home, staggered RTL altitudes and a long enough RTL loiter, all exported as `.parm` files.
7. The transition safety gate rejects crossing assignments.
8. Fractional holds go out as a whole-second waypoint hold plus a `NAV_DELAY`, instead of being truncated by the vehicle.
9. The S-curve acceleration limits are modelled and pinned, and the automatic scale keeps its 1.2 × margin for the legs as flown. With constant-speed legs the model had overstated the closest approach: 5.58 m became 5.06 m once S-curves were counted.
10. The `.waypoints` rows are numbered after the home row. They used to start at 0, next to home.
11. The Python MAVLink script is replaced by `mavlink_show.fsx`, which refuses to fly unless the vehicles carry exactly the checked mission and parameters.

The pack is decision support for a safety case, not a substitute for one.

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

The `CollisionAvoidance` module is an **optional extension** that *reduces* collision risk during formation transitions by staggering **when** each drone moves. The main solver optimizes **which drone goes where**, but not the timing; with simultaneous straight-line paths, drones might collide mid-flight.

> **Limitation:** this module only adjusts per-drone start delays along otherwise-fixed straight-line paths. Timing offsets reduce simultaneous proximity but **cannot guarantee** separation for geometrically crossing paths — that would require spatial re-routing. Every plan reports an `IsSafe` flag and a `MinAchievedSeparation`; when `IsSafe` is false a residual collision remains and the plan is labelled `[UNRESOLVED COLLISION]`. Always check `IsSafe` before flying.

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
- **Risk reduction, not a safety guarantee**: use it to lower collision risk, but do not rely on it as the sole safeguard for safety-critical flight — verify `IsSafe` and add spatial deconfliction where separation must be assured

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
8. **MAVLink for .NET** (NuGet `MAVLink`, ArduPilot Mission Planner) - https://github.com/ArduPilot/MissionPlanner/tree/master/ExtLibs/Mavlink

### Quantum Computing
10. **QAOA** - Farhi et al. (2014) - arXiv:1411.4028
