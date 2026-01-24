# Drone Swarm Choreography

**Quantum-Optimized Formation Planning with Real Drone Export**

This example demonstrates quantum optimization using QAOA to solve the Quadratic Assignment Problem (QAP) for drone formation transitions. It uses 4 drones (16 qubits) which fits within the LocalBackend's 20-qubit limit, enabling actual quantum execution and **real drone automation** through Crazyflie export.

## RULE 1 Compliance

This example is **fully RULE 1 compliant**:
- All optimization uses QAOA via `IQuantumBackend`
- Classical greedy is only used as internal fallback if quantum solver fails
- No standalone classical solver exposed in public API

## Key Features

- **16 qubits** (4 drones x 4 positions) - fits LocalBackend limit
- **Quantum solver executes** - actual QAOA optimization
- **Crazyflie export** - generates executable Python scripts for real drones
- **JSON waypoints** - standard format for custom integrations

## Quick Start

```bash
# Build and run with quantum optimization
dotnet run -- --shots 2000

# Run with Crazyflie export
dotnet run -- --export

# Custom export parameters
dotnet run -- --export --scale 0.1 --duration 5.0
```

## Command Line Options

| Option | Default | Description |
|--------|---------|-------------|
| `--shots` | `2000` | Number of quantum measurements |
| `--out` | `runs/drone/swarm` | Output directory |
| `--export` | (flag) | Generate Crazyflie Python script |
| `--scale` | `0.05` | Position scale factor for indoor use |
| `--duration` | `3.0` | Transition duration in seconds |

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
║  RULE 1 COMPLIANT: Quantum solver via IBackend  ║
║  Quantum solved: 4 | Fallback used: 0            ║
╚══════════════════════════════════════════════════╝

╔══════════════════════════════════════════════════╗
║  CRAZYFLIE EXPORT                                ║
╚══════════════════════════════════════════════════╝
Wrote JSON waypoints to: runs/drone/swarm/crazyflie_show.json
Wrote Python script to: runs/drone/swarm/crazyflie_show.py
```

## Files Generated

| File | Description |
|------|-------------|
| `metrics.json` | Performance metrics (JSON) |
| `run-report.md` | Human-readable summary |
| `crazyflie_show.json` | Waypoint data for custom integrations |
| `crazyflie_show.py` | Executable Crazyflie Python script |

## Quantum Computing Notes

### RULE 1 Architecture

```fsharp
// RULE 1 COMPLIANT: Backend required, no standalone classical
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

## See Also

- [FleetPathPlanning](../FleetPathPlanning/README.md) - TSP for drone delivery routes
- [SwarmTaskAllocation](../SwarmTaskAllocation/README.md) - Job shop scheduling for drone tasks

## References

1. **Crazyflie Documentation** - https://www.bitcraze.io/documentation/
2. **Lighthouse Positioning** - https://www.bitcraze.io/documentation/system/positioning/lighthouse/
3. **cflib API** - https://www.bitcraze.io/documentation/repository/crazyflie-lib-python/
4. **QAOA** - Farhi et al. (2014) - arXiv:1411.4028
