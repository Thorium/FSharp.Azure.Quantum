# Flying the drone examples in ArduPilot SITL

All four drone examples export ArduPilot missions with `--mavlink`. The
generated `mavlink_show.fsx` flies them over MAVLink 2, and it also draws the
flight. This page shows how the missions were flown in ArduPilot's own
software-in-the-loop simulator (SITL). It also covers what that changed in the
examples. SITL runs the real autopilot firmware against a simulated airframe.
It is the step before a real aircraft, not a substitute for one.

## Setup

ArduPilot publishes prebuilt SITL binaries, so nothing needs building. On Linux
or WSL:

```bash
mkdir -p ~/ardupilot-sitl && cd ~/ardupilot-sitl
curl -fLo arducopter https://firmware.ardupilot.org/Copter/stable/SITL_x86_64_linux_gnu/arducopter
curl -fLo arduplane  https://firmware.ardupilot.org/Plane/stable/SITL_x86_64_linux_gnu/arduplane
chmod +x arducopter arduplane
for f in copter.parm quadplane.parm; do
  curl -fLo $f https://raw.githubusercontent.com/ArduPilot/ardupilot/master/Tools/autotest/default_params/$f
done
```

Start one simulator per vehicle at the home the launcher's dry run prints.
Instance *i* listens on TCP port 5760 + 10 *i*, which is the launcher's default
connection:

```bash
~/ardupilot-sitl/arducopter --model + --defaults ~/ardupilot-sitl/copter.parm -w -I0 --home 37.7749,-122.4192862,0,0
```

A QuadPlane uses `arduplane --model quadplane --defaults quadplane.parm`.
Start every simulator from one shell and keep that shell waiting: a simulator
started from a WSL process of its own exits when the first client connects.

Then fly, check and draw:

```bash
dotnet fsi mavlink_show.fsx --dry-run      # every message, no network
dotnet fsi mavlink_show.fsx --speedup 4    # fly against SITL run with --speedup 4
dotnet fsi mavlink_show.fsx --check        # flown against planned, and the picture
dotnet fsi mavlink_show.fsx --draw         # the picture only: plan.svg before a flight
```

`--speedup` must match the simulators' own `--speedup`. It scales the
launcher's clock, its heartbeat and its polling.

## The pictures

`--draw`, `--check` and every finished flight write an animated SVG next to the
missions: `flight.svg` after a flight, `plan.svg` from the plan alone. The
drawing is part of the shared launcher, so it works for every example and any
fleet size. It reads only `plan_tracks.csv` and `telemetry.csv`. It shows three
views, one below the other:

- **From above, north up.** Each planned track is a wide pale band in its
  aircraft's colour, and the flown track a thin line on it, so a flight that
  keeps to its plan stays inside the band. The closest approach is circled. A
  show flown in a vertical plane is drawn from the side instead.
- **Height over time.**
- **A 3-D view from the south-west**, with heights exaggerated when the flight
  is much wider than it is high.

Each aircraft moves through all three views as an icon in its colour: a copter
as four rotors on an X, a fixed-wing as a plane turned the way it flies,
faint while on the ground. A cursor sweeps the height chart and a clock runs.
The whole flight plays in 20 s and loops. It is drawn
with [VectSharp](https://github.com/arklumpus/VectSharp) and its 3-D module,
with SVG animation added to VectSharp's output. It plays in any browser,
including as an image in a README.

## What SITL changed

Each of these was found by flying, not by reading.

- **ArduPilot 4.7 renamed parameters to metres.** Examples are `RTL_ALT_M`,
  `WP_SPD`, `WP_RADIUS_M` and `LAND_SPD_MS`. Every exported parameter file used
  the old names, and the firmware never answers a write to a name it does not
  have. The launcher now refuses a vehicle that lacks a parameter, and says
  which one.
- **The launcher could not fly at all.** ArduPilot asks for mission items with
  `MISSION_REQUEST`, and the launcher waited only for `MISSION_REQUEST_INT`.
  Vehicles report position 0, 0 before their GPS has a fix. AUTO does not take
  off from the ground without a raised throttle unless `AUTO_OPTIONS` allows
  it.
- **The plans assumed constant speed.** A copter accelerates and decelerates on
  every leg under its S-curve limits. It spools up for 3 to 4.5 s before a
  take-off. At a waypoint with no hold it carries its speed through the corner.
  Two aircraft came 0.07 m apart over a drop slot because their timing had
  drifted by 20 s.
- **Now every copter waypoint holds 1 s, so the copter comes to rest.** The
  model times each leg as a rest-to-rest S-curve, plus 2.2 s per stop: 1.2 s
  to reach the waypoint, measured over 20 legs, and the 1 s hold. A 4 s
  spool-up is counted at each launch. The slack left for timing error is the
  2 s start tolerance plus 0.3 s per leg and 2% of the time flown.
- **A fixed-wing's position on its loiter circle cannot be planned.** It joins
  the circle wherever it arrives. The checks now treat the whole circle as
  occupied while it circles.
- **A QuadPlane slows down for its VTOL landing.** Its leg into the landing
  took 11 to 12 s longer than a cruise leg, over the last 1.5 km. The model now
  counts 12 s. It also flies the approach at the height it holds and descends
  only over the landing point, which the model now does too.
- **A QuadPlane turns on a circle, not on a point.** With `WP_RADIUS` equal to
  its 64 m turn radius it cuts a corner of up to 90 degrees on an arc and
  rejoins the next leg; a sharper one swings outside the next leg first. The
  planned tracks now draw those turns, measured against a flight at seven
  corners.
- **A QuadPlane's RTL is a fixed-wing flight home.** It changes height on the
  way instead of climbing in place, and it crosses the base at its RTL height
  while copters climb straight up over their pads. Its RTL height is now at the
  ceiling, above every copter's climb. ArduPlane keeps `Q_RTL_ALT` in whole
  metres, and the launcher refused a plan that asked for 121.92 m.
- **An aircraft that cannot get home still flies its RTL.** The exported
  failsafes return on low battery and land on critical battery. The dropout
  checks used to send such an aircraft to a termination zone or straight down.
  Now they check the RTL home and the RTL cut where the range runs out, with a
  landing there.
- **The simulated quad's default airframe tops out near 10 m/s** at its default
  30° lean limit. The fleet files rate the QuadX-200 at 12 m/s. For the runs,
  the simulated airframe was given a 45° limit (`ATC_ANGLE_MAX 45`) so that it
  can fly its rated speed. The exported parameter files do not set it: a real
  airframe must be able to fly its rated speed as it is.

## What SITL does not show

SITL has no wind unless it is asked for, no GPS error beyond its defaults, no
radio loss, and no other traffic. The missions have not flown on a real
aircraft. Before one does, fly the same missions in SITL with wind and GPS
noise (`SIM_WIND_*`, `SIM_GPS*`), then with one real aircraft, and with the
operator's own airframe parameters.
