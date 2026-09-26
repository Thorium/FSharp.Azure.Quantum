# Air Bridge: forest fire, or a demo in a room

Drones shuttle from sources to targets, and the plan keeps changing while they
fly: the wind shifts, smoke closes a lake, embers start a spot fire, a target
is finished, aircraft drop out.

```bash
dotnet run --project examples/Drones/FireAirBridge
```

The default scenario is a forest fire: water from lakes to hotspots, over an
hour, at kilometre scale. The same program flies four micro-drones from a pad
to ten cans in a room, over four minutes, at metre scale (see [The can demo](#the-can-demo)).

## What this is for (and what it is not)

Water is 1 kg per litre and multirotors carry mass badly. The default fleet is
60 Agras-class aircraft carrying 40 L each, one sortie per battery. It moves a
few hundred litres a minute. A medium helicopter with a bucket moves many times
that. Many small drops also don't add up to one big drop: water arriving at a
trickle mostly evaporates.

So the scenario is **hotspot suppression and mop-up at about 1 ha per sector,
at night or in smoke when helicopters are grounded**. That is where an aircraft
with nobody in it changes the risk calculus. It is not a replacement for
aircraft on a running front. The run shows this: the fire spreads under every
policy, and the plan decides how much.

## The model: the corridor is the unit, drones are flow

A drone is neither a node nor a path. The planned entity is the **corridor**:
source → outbound lane → drop over a target → return lane → source. Drones
circulate on it, and Little's law ties the two together:

```
drones on a corridor = drops per tick × cycle time
```

A corridor's throughput is capped by the narrowest part of the pipe: the
source's fill slots, the lane's in-trail spacing, or the drop slots at the
target. Past that cap, extra drones deliver nothing. The run starts by printing
where each source saturates.

Corridor choice is combinatorial because of the people and the physics, not
the drones:

| Constraint | Why it matters |
|---|---|
| **Ground crews** (`--crews`, default 2) | Every source in use needs a crew for fills and battery swaps. Relocating a crew takes `--crew-move` ticks (default 6). |
| **Drop coordinators** (`--coordinators`, default 5) | Every open corridor needs one at the targets. |
| **Concentration floor** (`MinSalvoLpm`, 100 L/min, fire only) | Below this rate, water on a burning sector mostly evaporates. Drones must be massed on a few hotspots, so two corridors into one sector can be worth far more than twice one. Pre-wetting unburnt fuel has no floor. |
| **Warm-up** | A new corridor delivers only after its pipe fills and its crew arrives. That makes every plan change cost something real. |
| **Terminal legs** | A cycle is more than the lane: the climb off the pad, the radial leg from the pad ring to the source centre, the legs to and from the drop slot, the climb to the return altitude and the descent onto the pad. The corridor model counts them, sized for the whole fleet on one pad ring, so a plan never assumes a shorter cycle than a real aircraft flies. For the fire fleet this is about 90 s of a 7-minute cycle, and it is what makes the longest corridors one cycle per battery. |
| **Wind** | A tailwind on the way out is a headwind on the way back. It changes cycle times, endurance margins and lane capacity. |
| **Traffic** | A lane that leaves the plan drains for one more cycle. A new corridor may not be opened across a lane that is flying or draining, so a target behind a finished one waits until the airspace is clear. |

## Scale-agnostic: frame, tick, demand, lane geometry

The planner works on a flat plane in km and counts time in ticks. Three
discriminated unions decide what those mean, and the fleet file carries the
lane geometry:

| | Options | How it is chosen |
|---|---|---|
| **Frame** | `Geodetic`: `latitude`/`longitude` columns, projected around the centroid, the pilot at the centroid. `LocalMetres`: `x_m`/`y_m` columns in metres, the pilot at the origin. | From the CSV columns. All files must use one frame. |
| **Tick** | `Minute` or `Second`. Every duration option, every event time and every rate is in ticks. | `--tick minute` (default) or `--tick second`. |
| **Demand** | `Fire`: sectors want litres; intensity grows, spreads and is knocked down (`FireSim`). `Touches`: targets owe `touches` drops each and want them within `--touch-target-s` seconds; a drop is a whole thing, so a half-served target keeps its full demand until it is done. | `--demand fire` (default) or `--demand touches`. |
| **Lane geometry** | `lane_spacing_m` (in trail), `lane_base_alt_m`, `lane_layer_step_m`, `lane_return_offset_m`, `min_separation_m`, `min_vertical_m`, `swap_time_s`. | Optional fleet columns; the defaults are the outdoor values (30 m, 40 m, 20 m, 10 m, 10 m, 5 m, 180 s). `--ceiling-m` sets the altitude ceiling the evidence checks. |

Positions, speeds and spacings keep their physical units in both frames: a
10 m lane at 1 m/s takes 10 s whether the file said metres or degrees. The
fire physics are quoted per minute and scaled to the tick.

## Two loops

- **Fast loop.** Classical, runs every tick, takes milliseconds. It spreads
  the fleet over the open corridors as demand shifts, and abandons sectors it
  can't reach the concentration floor on. The drop point moves along the front
  while the corridors stay put. Its score is also the ground truth every
  candidate plan is judged by.
- **Slow loop.** Runs every `--replan-every` ticks (default 10), on every
  event, when a target is finished, and when a draining lane clears. It
  decides which corridors to open. The quantum planner uses one qubit per
  candidate corridor plus one per source when the sources outnumber the crews.
  The QUBO is the pairwise expansion of the fast loop's score, plus penalty
  terms for the crew and coordinator limits and for conflicting lanes. QAOA
  angles from the previous round warm-start the next one: 5 circuits instead of
  a 40-point grid, which works because the QUBO is normalised to the same scale
  every round.

**Anytime planning.** The slow loop never waits for a quantum answer. At a
re-plan it takes the instant greedy plan if that beats what is flying. The QAOA
plan arrives `--latency` ticks later, or after its measured compute time if
that is longer. It is re-scored against the conditions at arrival and replaces
the current plan only if it still wins and opens nothing across traffic.

## Measured, not assumed

The same fire and events run under four policies:

| Policy | What it does |
|---|---|
| `static` | Plans once at t=0 with the best available planner, then never re-plans. |
| `adaptive-greedy` | Re-plans with the classical greedy only. |
| `adaptive-exact` | Re-plans with brute force. This is an oracle, possible only at simulable sizes. |
| `adaptive-hybrid` | Takes greedy now and QAOA after the latency. This is the proposed design. |

A default run on the local simulator gave these results. Lower fire ha·min is
better; it is burning area weighted by intensity, summed over minutes.

| Policy | Fire ha·min | Peak ha | Water (L) |
|---|---|---|---|
| static | 168.9 | 3.58 | 3260 |
| adaptive-greedy | 171.7 | 3.87 | 3074 |
| adaptive-exact | 171.7 | 3.87 | 3074 |
| adaptive-hybrid | 171.7 | 3.87 | 3074 |

What the numbers say:

- **With this fleet, adapting does not pay.** A 10-minute battery gives one
  cycle per sortie on the longer corridors once the terminal legs are
  counted, so a new corridor delivers nothing for about three minutes and
  every plan change costs a large share of the 20-minute horizon. The static
  plan keeps flying; the adaptive policies switch lakes on the wind shift and
  the smoke closure and lose more warm-up than they gain. Before the terminal
  legs were counted the same scenario read the other way (static 165.7,
  adaptive 145.2 ha·min): that result was flying cycles no aircraft can fly.
  A longer-endurance fleet, or fewer plan changes, would change it back.
- **At this size the combinatorics are shallow.** With 7–13 qubits, greedy
  and QAOA (p=1) both matched the oracle in every round of the default run.
  QAOA's measurement sampling is random, so it misses in some runs. At
  simulable sizes a classical planner is enough. The case for quantum hardware
  rests on scale: dozens of water points (lakes, portable tanks, hydrants),
  dozens of sectors, several crew types. That grows past brute force quickly.
  This example does not demonstrate an advantage there; it sets up the
  pipeline to measure one.
- **Larger instances are where it gets hard, and slow.** One synthetic
  scenario had 7 lakes, 12 sectors, 3 crews and 16–19 qubits. There greedy
  missed the oracle in 2 of 5 rounds, and QAOA p=1 matched it in none.
  - A cold round (41 circuits) took about 3.5 min on the local simulator.
  - A warm round took 15–30 s.
  - The simulated answer can never arrive before its measured compute time,
    so slow rounds cost the plan real mission minutes.
  - Default runs finish in seconds; expect minutes per round from about 18
    qubits.
- **Committing early has a price.** At t=0 the better QAOA plan arrived one
  minute after greedy had been adopted. By then a crew was already heading to
  greedy's lake, and switching no longer paid. The obvious remedy is not in the
  code yet: fly the instant plan's drones, but hold crew moves until the slower
  answer arrives. Crews are the expensive thing to move.

## The can demo

Four Crazyflies start on pads at the origin. Ten cans stand about 10 m away in
a 2.4 × 0.7 m patch, two rows of five, 0.6 m apart along a row and 0.7 m
between rows. Each can must be touched once, and no two drones may come within
0.5 m of each other.

```bash
dotnet run --project examples/Drones/FireAirBridge -- \
  --sources examples/Drones/_data/cans_pad.csv \
  --sectors examples/Drones/_data/cans_targets.csv \
  --fleet examples/Drones/_data/cans_fleet.csv \
  --events examples/Drones/_data/cans_events.csv \
  --tick second --ticks 240 --demand touches --horizon 60 --replan-every 15 \
  --crews 1 --coordinators 3 --crew-move 0 --ceiling-m 2 \
  --mavlink --home-lat 60.1699 --home-lon 24.9384 \
  --out runs/drone/cans-air-bridge
```

The pad is the source (four "fill" slots, a 3 s turnaround hover). Each can is
a target owing one touch. The fleet file sets the indoor limits from
SwarmChoreography's room layout: 0.5 m in trail, 1.0 m vertical (downwash),
outbound lane at 0.8 m and return lane at 1.8 m under a 2 m ceiling, a 60 s
battery swap, 1 m/s. The events lose one drone at t=60 s and make can 3 need
touching again at t=90 s. `--mavlink` makes whole aircraft fly it (see the
next section) and writes their ArduPilot missions.

What the model says about the demo:

- **The back row cannot be served with the front row.** A lane from the pad to
  a back-row can passes within centimetres of the front-row can in line with
  it, and a drone touching the front can would sit under that lane. The
  planner refuses such pairs, so it works the front row and the back row in
  turns. Cans 0.6 m apart in one row are fine: their lanes are 0.5 m apart
  8.6 m out from the pad, inside the lane length.
- **A finished can's lane stays busy for one more cycle.** The can behind it
  is opened only when that lane is clear, which the run reports as
  `PAD>C3 clear`.
- **All ten cans, and the retouched one, are touched by whole aircraft by
  t=185 s**: 14 sorties of one cycle each, 15 s on the ground between an
  aircraft's sorties, one aircraft lost mid-sortie at t=60 s and written off
  where it was. The closest any two come is 0.5 m (the limit), stacked one
  vertical minimum apart over a drop slot. The static plan touches the three
  cans it opened and stops. The evidence pack PASSes.
- **The physical caveats stand**: touching a can at 10 m needs indoor
  positioning, and "touch" means hovering just above it. The missions are
  ArduPilot's; a Crazyflie would need a cflib export of the same sorties,
  which this example does not have.

At this size the slow loop is trivial (1–10 qubits, greedy matched the oracle
in every round). The demo shows the deconfliction, the re-planning and the
flights, not a quantum advantage.

## From flow to flights: `--dispatch` and `--mavlink`

The planner's answer is a rate: 1.3 aircraft circulate on a corridor. Nobody
flies 1.3 aircraft, and a plan nobody can follow is worth nothing. With
`--dispatch` the simulation is driven by whole aircraft instead:

- **Sorties.** Each second, the dispatcher compares the flow's rounded
  allocation with the aircraft already on each corridor and launches one when
  short: one aircraft, one corridor, a whole number of cycles (enough to
  finish a target's touches, or as many as the battery holds for a fire).
  Only the drops those sorties land count as delivered, so a target is done
  when a real drop lands, not when a fraction accumulates.
- **Geometry.** Every aircraft has its own pad on an arc around its home
  source, facing away from the source's targets, and fills above it. Drop
  slots fan out around the target on the side away from the lane, so none
  sits in the approach, and the ring is wide enough that the radial into one
  slot clears an aircraft hovering in the next. Every leg is vertical at the
  pad, radial to a centre, or a lane, so legs meet only at the centres.
- **Time on the ground and in the air.** The landing is ArduPilot's: the
  descent speed down to 10 m, then `LAND_SPEED` onto the pad. Between two
  sorties an aircraft sits 15 s on its pad: the 5 s disarm delay the
  parameter file sets, then the launcher's upload, read-back, arm and start.
  A lost aircraft's sortie ends where it is: its track stops, its later drops
  never land, and its bookings stay so nobody else takes them.
- **Booking.** Before a sortie launches, every passage of every cycle through
  the source and target centres, every fill and every drop-slot occupancy is
  booked against what is already booked: a headway apart on the same lane,
  the merge geometry apart for lanes meeting at an angle, the drop slot held
  until the aircraft has climbed clear, with `--stagger-margin` (1.5) over
  all of it for the autopilot's acceleration. A cycle that cannot be booked
  ends the sortie before it; a launch that cannot be booked waits a second.
- **Two checks join the pack.** The exact closest approach between every pair
  of aircraft over the whole run, heights scaled so that the fleet's vertical
  minimum counts as its horizontal minimum, parked aircraft included. And
  follow-through: for touches, every target got its touches from whole drops;
  for a fire, the share of the plan's aircraft-seconds that whole aircraft
  flew, with 75% as the example's working threshold.
- **Missions.** `--mavlink` writes one ArduPilot mission per sortie under
  `mavlink/`: take-off, then per cycle the fill hold above the pad, the lane
  out at the outbound altitude, the drop slot, the lane back at the return
  altitude, above the pad, repeated with `DO_JUMP`, then land on the pad. The
  lane legs command the ground speed the tracks were checked at, wind
  included, since a copter's `DO_CHANGE_SPEED` is a ground speed. Each comes
  as a QGroundControl `.plan`, a `.waypoints` file and a `.parm` file (speeds,
  `WPNAV_RADIUS`, `DISARM_DELAY`, `FS_OPTIONS` = continue the mission on lost
  link, as the failsafe procedure models; one parameter set per vehicle).
  `mavlink_show.fsx` is the launcher shared with SwarmChoreography: it sets
  and reads back every parameter, uploads and reads back every first sortie,
  checks each vehicle stands on its pad, refuses to start on any difference,
  starts everything due at T0 together, and flies each later sortie at its
  planned time once the vehicle has landed and disarmed. A sortie that would
  start more than 5 s late is not flown: its slots were booked for its time.
  `dotnet fsi mavlink_show.fsx --dry-run` prints every message without a
  network. A local frame needs `--home-lat` and `--home-lon` for the pads'
  geodetic position; a geodetic scenario uses its own centroid.

What dispatching the fire scenario shows (`--dispatch --policy adaptive-hybrid`):

- 150 sorties, 142 drops, 5680 L, 48 aircraft airborne at the peak; the
  closest approach is 10.2 m, on the pad ring.
- **Whole aircraft flew 62% of the plan's aircraft-seconds**, and the
  follow-through check FAILs. The plan asks for 39 aircraft on one corridor
  from the first minute; a lake with 3 fill slots and a 30 s fill launches one
  aircraft every 10 s, so the ramp alone takes six minutes, and every plan
  change starts another ramp. The flow model's Little's-law allocation is a
  steady state; it has no ramp. That is the operator's decision to make (more
  fill slots, fewer plan changes, a longer horizon), and the number that
  makes it.
- An aircraft is homed at one source for the run. A plan that moves the fleet
  to another lake shows up as a shortfall, not as a ferry flight.

The launcher has never met a vehicle or ArduPilot SITL; the mission timing is
constant speed per leg, with the margin standing in for the autopilot's
acceleration. Try it against SITL before any aircraft.

## One-pilot-to-many permission evidence

The default rule is one pilot per aircraft. A one-to-many (1:N) permission
requires the operator to show how the aircraft stay deconflicted, in range and
under the altitude ceiling, and what happens when one drops out.
`permission-evidence.md` and `.json` answer this for the operation as flown.
Every tick of the proposed policy's run is checked, not only the final plan:

| Area | How it is evidenced here |
|---|---|
| Deconfliction | Lane spacing vs. the fleet's in-trail minimum, and lane load vs. headway capacity, every tick. The planner refuses lanes that cross, overlap or pass within one lane spacing of each other anywhere, including a lane that passes over another lane's drop zone or source, because a failing drone descends straight down and would fall through a lower lane. Each corridor's outbound and return lanes are `lane_return_offset_m` apart vertically, checked against `min_vertical_m`, and a lane keeps its altitude for as long as it flies. When a corridor leaves the plan, its drones stay in the checks for one cycle while they finish, and nothing new is opened across them. |
| Endurance | For each corridor, cycles per battery × cycle time at the worst wind it flew in, against endurance minus the 15% reserve. If a wind shift makes a planned corridor unflyable while drones are on it, those drones are stranded and the pack FAILs. |
| C2 link | Every source and target against a 900 MHz link budget with a 10 dB fade margin, measured from the pilot station: the incident command post at the centroid of a geodetic scenario, the origin of a local frame. |
| Altitude | Every lane altitude against `--ceiling-m`. |
| Drop-out | Losing aircraft mid-mission (the fleet re-spreads the same tick, and later ticks are still checked), losing a source (no lane flies from a closed source), and an aircraft failing inside a lane (no lane below it). |
| Ratio | Declared pilots against fleet size and peak airborne (`--pilots`). |
| Supervisor workload | Each scripted event (aircraft lost, source closed, wind shift, spot fire or retouch) and each single-aircraft failsafe is a scenario, resolved either automatically or by a pilot decision. The fire run is fully hands-off: at worst 15 aircraft are off-nominal at once, with no decisions. A failsafe the configuration baseline doesn't set up as modelled becomes a pilot decision. |
| Terminal areas | Every lane into a source or drop zone merges into one in-trail sequence before the slots. Each tick the pack checks three things: the merged drops per tick fit one lane's headway capacity; the merge point, where two lanes are one spacing apart, lies inside the shorter lane; and the fill or drop slots sit on a ring whose radius puts neighbouring slots one spacing apart. The ring radii are listed, ready for laying out the slots. |
| Failsafes | The deconflicted way home is to finish the current cycle along the lanes to the source's recovery slot. Those lanes are in every check, and a whole cycle fits the usable battery. The operator's configuration baseline (`--vehicle-config`, default `fire_vehicle_config.csv`) is checked against that procedure for lost link, its timeout, low battery and in-lane failure. `rtl` fails because it flies straight home across other lanes. `smart_rtl` fails because it retraces the outbound lane against traffic. |

A run in which nothing flew FAILs its first check rather than passing every
other one vacuously, and a fire scenario in which nothing can burn is refused
before it runs.

The pack reports **NOT EVIDENCED** where the model can't show something,
instead of passing it. Without a configuration baseline, the failsafe items
stay NOT EVIDENCED. With one, the evidence covers the configuration the
operator declares. Checking that the loaded parameters match it remains a
preflight procedure. An assessor who finds a gap costs more than an operator
who declared it.

## Options

Run with `--help` for the full list. The main options are:

| Option | Meaning |
|---|---|
| `--tick minute\|second` | The unit every duration below is counted in (default minute). |
| `--ticks <n>` (or `--minutes <n>`) | Simulated ticks (default 60). |
| `--demand fire\|touches`, `--touch-target-s` | What the targets want; for touches, the seconds an open target should be finished in (default 30). |
| `--ceiling-m` | Altitude ceiling for the evidence (default the regulatory AGL ceiling). |
| `--dispatch` | Whole aircraft fly the plan; adds the closest-approach and follow-through checks and writes `sorties.csv`. |
| `--mavlink`, `--home-lat`, `--home-lon`, `--home-alt` | Also write one ArduPilot mission per sortie and the launcher (implies `--dispatch`). A local frame needs the home position. |
| `--stagger-margin` | Booked passages are this many headways apart (default 1.5). |
| `--policy all\|static\|adaptive-greedy\|adaptive-exact\|adaptive-hybrid` | Which policies to run. |
| `--crews`, `--coordinators`, `--crew-move` | People on the ground. |
| `--max-corridors` | Candidate corridors per round; QAOA adds one qubit per source. |
| `--latency`, `--layers`, `--shots` | QAOA answer delay (ticks), depth p, and final shots. |
| `--horizon` (default 20), `--replan-every` (default 10) | Planning horizon and periodic re-plan interval, in ticks. The horizon must exceed a corridor's warm-up or nothing is worth opening. |
| `--pilots` | Pilots declared in the permission evidence. |

## Data

These files live in `examples/Drones/_data/`:

| File | Contents |
|---|---|
| `fire_water_sources.csv` | Lakes and reservoirs (latitude/longitude), with fill slots and fill time. |
| `fire_sectors.csv` | Hotspot sectors: area, initial intensity, asset priority (cabins = 3), drop slots, neighbours. |
| `fire_fleet.csv` | The drone class: payload, loaded and empty speed, endurance, lane spacing; lane geometry and separation minima are optional columns. |
| `fire_events.csv` | Timed events (`minute` or `tick`): wind direction and speed, source closed or reopened, spot fire or retouch, drones lost. |
| `fire_vehicle_config.csv` | The operator's failsafe configuration baseline, checked by the evidence pack. |
| `cans_pad.csv`, `cans_targets.csv`, `cans_fleet.csv`, `cans_events.csv` | The can demo (x_m/y_m), see above. Target rows need only an id, a name and a position; `touches` defaults to 1. |

## Outputs

Written to `--out` (default `runs/drone/fire-air-bridge/`):

| File | Contents |
|---|---|
| `timeline.csv` | Per policy and tick: the demand metric (fire ha or touches left), units delivered, drones flying and idle, open corridors, per-target progress. |
| `decisions.csv` | Every slow-loop decision, with incumbent, greedy, exact and QAOA scores, qubits, circuits, warm start and time. |
| `lanes.csv` | The last air bridge flown, with altitude layers. |
| `sorties.csv` (`--dispatch`) | Every sortie: aircraft, corridor, launch and landing second, cycles, altitudes, when its drops land. |
| `mavlink/` (`--mavlink`) | Per sortie `<aircraft>_s<n>_mission.plan`, `.waypoints` and `.parm`, plus `mavlink_show.fsx`. |
| `metrics.json` | Tick, demand, frame, policy summaries and QAOA statistics. |
| `permission-evidence.md`, `permission-evidence.json` | The 1:N evidence pack. |

The fire model is deliberately small and its numbers are illustrative, not
calibrated fire science. The default limits are this example's working values
from `DroneDomain.fs`, not any authority's rule; an indoor fleet declares its
own in its fleet file.
