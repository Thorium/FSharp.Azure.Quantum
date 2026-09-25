# Drone Fleet Path Planning

A TSP solver orders the waypoints; the fleet then flies that order in sorties
from a base.

```bash
dotnet run --project examples/Drones/FleetPathPlanning
```

## Sorties

The tour includes the base and is rotated to start there. `FleetPlanner`
splits it into sorties in fleet order. Each sortie must fit the whole flight
into the drone's `MaxRangeKm` minus the battery reserve: out to the first
waypoint, the legs, and home again.

A waypoint that no drone can reach is reported, and planning carries on with
the rest.

The planner measures from the base. In the evidence tracks every aircraft has
its own pad on the ground, on a ring around the base. Neighbouring pads are at
least two minimum separations apart. An aircraft climbs vertically from its
pad to the base's altitude, and when it returns it descends vertically back
onto its pad. Its fallback also goes home to that pad. Aircraft waiting on
their pads before launch, or parked after landing, count in the separation
check, so a landing aircraft is compared with the ones parked next to it. The
pad offset and the vertical climb and descent add about a hundred metres per
flight, which fits inside the battery reserve. The pack's Assumptions give
the figures.

Drone rows with `max_range_km` or `cruise_speed_ms` at or below zero are
parse errors.

**Coming back is a mission decision.** With `--needs-return false` the
mission flies one way and the aircraft are written off. Where they come down
is not left to chance:

- The operator declares termination zones in `termination_zones.csv`: fields,
  a quarry, open water, each with a centre, an arrival height and a radius.
- Each one-way sortie ends with a leg to the zone nearest its last waypoint.
  That leg counts against its range.
- The aircraft then makes a controlled descent. The evidence checks that the
  descent's drift, at the maximum operating wind, stays inside the zone's
  radius.
- A one-way aircraft that drops out mid-flight and can't get home goes to the
  nearest zone rather than coming down where it is.

## C2 relay mesh

A 2.4 GHz link reaches about 2 km with a 10 dB fade margin. When the mission
reaches farther, the drone that lifts the most becomes the **relay carrier**
and leaves the mission fleet. "Farther" is judged on the sampled flown paths
of a trial plan with the whole fleet, not on the waypoints alone. A one-way
sortie's leg to its termination zone can leave the link circle even when
every waypoint is inside it. If no mesh can be planned (no candidate sites),
the evidence FAILs C2 and says why.

The relay carrier:

- It flies first and sets repeaters down on sites from `relay_sites.csv`.
- It places each repeater only once the node linking it in (the base or an
  earlier repeater) is already down, so the mesh grows outward and the carrier
  never leaves it.
- The chosen sites cover every point of every flown path, not only the
  waypoints, within `Swarm.maxMeshHops` hops. Link circles don't join into a
  convex area, so a leg between two covered waypoints can still leave
  coverage.

The sites are chosen exhaustively for up to 12 candidates, with a greedy
fallback above that. They are ranked in this order:

1. Full coverage.
2. As few points as possible that depend on a single repeater. Losing one
   repeater must not cut anyone off. Coverage is never given up for
   redundancy.
3. The fewest repeaters.
4. The shortest deployment flight.

With the sample sites the mesh uses 6 repeaters, and none is a single point of
failure. `--minimal-relays` skips step 2 and uses 3, but each of those is a
single point of failure, and the evidence FAILs to say so.

## One-pilot-to-many permission evidence

`permission-evidence.md` and `.json` cover the plan as flown: the carrier
first, then every sortie.

| Check | What is evidenced |
|---|---|
| Coverage | Every waypoint is assigned to a sortie. |
| Separation | Exact closest approach between every pair of straight-line tracks. It is a closed form, not sampling, so two aircraft at one point at the same moment are caught at 0 m. Each aircraft flies from its own pad, and aircraft parked on their pads count. |
| Endurance | Every flight, including the carrier's, fits range minus reserve. |
| C2 | Every airborne second of every flight is within one link of a node that is live at that moment, within the hop limit. The carrier lifts every repeater. |
| Altitude | Waypoints, repeater sites and fallback layers stay under the ceiling. |
| Losing a drone before launch | Remove any one mission drone and re-plan: coverage, endurance, C2 and separation still hold. |
| Losing a repeater | No repeater is a single point of failure. |
| Losing the carrier | Another drone can deploy the mesh. |
| **Dropout in flight** | See below. |
| Pilot ratio | The pilots declared with `--pilots`. |
| Supervisor workload | Every dropout moment, repeater failure and pre-launch loss is a scenario. The fallback flight resolves itself. Diverting a neighbour after a knock-on conflict, and approving the follow-up sortie, are pilot decisions. The check fails when more decisions are needed at once than there are pilots. |

**A dropout in flight is expected, not a failure. What matters is that the
swarm absorbs it and doesn't fall like a house of cards.** Every 5 s along
every sortie, the aircraft leaves the plan while everyone else flies on
unchanged. Its fallback is to fly to its own RTL layer, straight to its pad, then
down. Each sortie's layer is `--rtl-step-m` above the previous one, starting at
`--rtl-base-m`. If the battery can't get it home, it descends in place instead.

The pack checks two things for each dropout moment:

- **No knock-on conflict.** The fallback never comes within minimum separation
  of another aircraft. If it did, the others would have to react, and that is
  how failures cascade.
- **The mission absorbs the loss.** The waypoints it never reached fit a
  follow-up sortie by the rest of the fleet after a battery swap.

In a crowded test plan the planned flights kept 16 m apart and passed. The
dropout check still failed: one fallback passed 2.3 m from a neighbour.

## Options

| Option | Default | Meaning |
|---|---|---|
| `--waypoints`, `--drones` | `_data/waypoints.csv`, `_data/drones.csv` | Mission and fleet |
| `--base <id>` | first waypoint | Centre of the ring of launch and landing pads, and the pilot station |
| `--launch-interval <s>` | 10 | Seconds between launches |
| `--needs-return <bool>` | true | `false` flies one way and expends the aircraft |
| `--relays <path>` | `_data/relay_sites.csv` | Candidate repeater sites |
| `--relay-mass-kg <m>` | 0.4 | Mass of one repeater |
| `--minimal-relays` | off | Fewest repeaters, accepting single points of failure |
| `--termination-zones <path>` | `_data/termination_zones.csv` | Accepted areas where one-way aircraft come down |
| `--rtl-base-m`, `--rtl-step-m` | 60, 10 | Fallback layers |
| `--pilots <n>` | 1 | Pilots declared in the evidence |
| `--method` | hybrid | `hybrid` or `quantum` TSP |

The limits are this example's working values from `DroneDomain.fs`, not any
authority's rules.
