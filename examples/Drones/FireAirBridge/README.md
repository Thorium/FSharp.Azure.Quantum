# Forest-Fire Air Bridge

Drones shuttle water from lakes to fire hotspots, and the plan keeps changing
while they fly: the wind shifts, smoke closes a lake, embers start a spot fire,
aircraft drop out.

```bash
dotnet run --project examples/Drones/FireAirBridge
```

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
lake → outbound lane → drop over a sector → return lane → lake. Drones circulate
on it, and Little's law ties the two together:

```
drones on a corridor = drops per minute × cycle time
```

A corridor's throughput is capped by the narrowest part of the pipe: the
lake's fill slots, the lane's in-trail spacing, or the drop slots at the fire.
Past that cap, extra drones deliver nothing. The run starts by printing where
each lake saturates.

Corridor choice is combinatorial because of the people and the physics, not
the drones:

| Constraint | Why it matters |
|---|---|
| **Ground crews** (`--crews`, default 2) | Every lake in use needs a crew for fills and battery swaps. Relocating a crew takes `--crew-move` minutes (default 6). |
| **Drop coordinators** (`--coordinators`, default 5) | Every open corridor needs one on the fire line. |
| **Concentration floor** (`MinSalvoLpm`, 100 L/min) | Below this rate, water on a burning sector mostly evaporates. Drones must be massed on a few hotspots, so two corridors into one sector can be worth far more than twice one. Pre-wetting unburnt fuel has no floor. |
| **Warm-up** | A new corridor delivers only after its pipe fills and its crew arrives. That makes every plan change cost something real. |
| **Wind** | A tailwind on the way out is a headwind on the way back. It changes cycle times, endurance margins and lane capacity. |

## Two loops

- **Fast loop.** Classical, runs every simulated minute, takes milliseconds. It
  spreads the fleet over the open corridors as demand shifts, and abandons
  sectors it can't reach the concentration floor on. The drop point moves
  along the front while the corridors stay put. Its score is also the ground
  truth every candidate plan is judged by.
- **Slow loop.** Runs every `--replan-every` minutes (default 10) and on every
  event. It decides which corridors to open. The quantum planner uses one qubit
  per candidate corridor plus one per lake. The QUBO is the pairwise expansion
  of the fast loop's score, plus penalty terms for the crew and coordinator
  limits. QAOA angles from the previous round warm-start the next one: 5
  circuits instead of a 40-point grid, which works because the QUBO is
  normalised to the same scale every round.

**Anytime planning.** The slow loop never waits for a quantum answer. At a
re-plan it takes the instant greedy plan if that beats what is flying. The QAOA
plan arrives `--latency` minutes later, or after its measured compute time if
that is longer. It is re-scored against the conditions
at arrival and replaces the current plan only if it still wins.

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
| static | 165.7 | 3.52 | 3666 |
| adaptive-greedy | 145.2 | 2.79 | 5075 |
| adaptive-exact | 138.3 | 2.71 | 5258 |
| adaptive-hybrid | 145.2 | 2.79 | 5075 |

What the numbers say:

- **Adapting pays.** Re-planning on events beats the best static plan clearly.
- **At this size the combinatorics are shallow.** With 7–13 qubits, greedy
  matched the oracle in 9 of 10 rounds. QAOA (p=1) matched it in 7–8 of 10;
  its measurement sampling is random, so this varies between runs. QAOA found
  the oracle's plan in the one round greedy missed (t=0), and fell short of
  greedy in others. At simulable sizes a classical planner is enough. The
  case for quantum hardware rests on scale: dozens of water points (lakes,
  portable tanks, hydrants), dozens of sectors, several crew types. That grows
  past brute force quickly. This example does not demonstrate an advantage
  there; it sets up the pipeline to measure one.
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

## One-pilot-to-many permission evidence

The default rule is one pilot per aircraft. A one-to-many (1:N) permission
requires the operator to show how the aircraft stay deconflicted, in range and
under the altitude ceiling, and what happens when one drops out.
`permission-evidence.md` and `.json` answer this for the operation as flown.
Every minute of the proposed policy's run is checked, not only the final plan:

| Area | How it is evidenced here |
|---|---|
| Deconfliction | Lane spacing vs. the in-trail minimum, and lane load vs. headway capacity, every minute. The planner refuses lanes that cross, overlap or pass within one lane spacing of each other, because a failing drone descends straight down and would fall through a lower lane. Each corridor's outbound and return lanes are 10 m apart vertically, and a lane keeps its altitude for as long as it flies. When a corridor leaves the plan, its drones stay in the checks for one cycle while they finish. |
| Endurance | For each corridor, cycles per battery × cycle time at the worst wind it flew in, against endurance minus the 15% reserve. If a wind shift makes a planned corridor unflyable while drones are on it, those drones are stranded and the pack FAILs. |
| C2 link | Every lake and sector against a 900 MHz link budget with a 10 dB fade margin, measured from the incident command post. |
| Altitude | Every lane altitude against the AGL ceiling. |
| Drop-out | Losing aircraft mid-mission (the fleet re-spreads the same minute, and later minutes are still checked), losing a lake (no lane flies from a closed lake), and an aircraft failing inside a lane (no lane below it). |
| Ratio | Declared pilots against fleet size and peak airborne (`--pilots`). |
| Supervisor workload | Each scripted event (aircraft lost, lake closed, wind shift, spot fire) and each single-aircraft failsafe is a scenario, resolved either automatically or by a pilot decision. The run is fully hands-off: at worst 15 aircraft are off-nominal at once, with no decisions. A failsafe the configuration baseline doesn't set up as modelled becomes a pilot decision. |
| Terminal areas | Every lane into a lake or drop zone merges into one in-trail sequence before the slots. Each minute the pack checks three things: the merged drops per minute fit one lane's headway capacity; the merge point, where two lanes are one spacing apart, lies inside the shorter lane; and the fill or drop slots sit on a ring whose radius puts neighbouring slots one spacing apart. The ring radii are listed, ready for laying out the slots. |
| Failsafes | The deconflicted way home is to finish the current cycle along the lanes to the lake's recovery slot. Those lanes are in every check, and a whole cycle fits the usable battery. The operator's configuration baseline (`--vehicle-config`, default `fire_vehicle_config.csv`) is checked against that procedure for lost link, its timeout, low battery and in-lane failure. `rtl` fails because it flies straight home across other lanes. `smart_rtl` fails because it retraces the outbound lane against traffic. |

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
| `--policy all\|static\|adaptive-greedy\|adaptive-exact\|adaptive-hybrid` | Which policies to run. |
| `--crews`, `--coordinators`, `--crew-move` | People on the ground. |
| `--max-corridors` | Candidate corridors per round; QAOA adds one qubit per lake. |
| `--latency`, `--layers`, `--shots` | QAOA answer delay (minutes), depth p, and final shots. |
| `--horizon` (default 20), `--replan-every` (default 10) | Planning horizon and periodic re-plan interval, in minutes. |
| `--pilots` | Pilots declared in the permission evidence. |

## Data

These files live in `examples/Drones/_data/`:

| File | Contents |
|---|---|
| `fire_water_sources.csv` | Lakes and reservoirs, with fill slots and fill time. |
| `fire_sectors.csv` | Hotspot sectors: area, initial intensity, asset priority (cabins = 3), drop slots, neighbours. |
| `fire_fleet.csv` | The drone class: payload, loaded and empty speed, endurance, lane spacing. |
| `fire_events.csv` | Timed events: wind direction and speed, lake closed or reopened, spot fire, drones lost. |
| `fire_vehicle_config.csv` | The operator's failsafe configuration baseline, checked by the evidence pack. |

## Outputs

Written to `runs/drone/fire-air-bridge/`:

| File | Contents |
|---|---|
| `timeline.csv` | Per policy and minute: active fire, water delivered, drones flying and idle, open corridors, sector intensities. |
| `decisions.csv` | Every slow-loop decision, with incumbent, greedy, exact and QAOA scores, qubits, circuits, warm start and time. |
| `lanes.csv` | The last air bridge flown, with altitude layers. |
| `metrics.json` | Policy summaries and QAOA statistics. |
| `permission-evidence.md`, `permission-evidence.json` | The 1:N evidence pack. |

The fire model is deliberately small and its numbers are illustrative, not
calibrated fire science. The evidence limits are this example's working values
from `DroneDomain.fs`, not any authority's rule.
