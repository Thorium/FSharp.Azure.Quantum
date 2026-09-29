/// Drone Swarm Task Allocation Example
///
/// This example demonstrates how to use FSharp.Azure.Quantum's TaskScheduling API
/// to allocate and schedule tasks across a fleet of drones with dependencies and
/// resource constraints.
///
/// DRONE DOMAIN MAPPING:
/// - Drone tasks (delivery, inspection, surveillance) → Scheduled Tasks
/// - Drone fleet → Resources with capacity constraints
/// - Task dependencies (must complete A before B) → Precedence constraints
/// - Mission completion time → Makespan objective
///
/// USE CASES:
/// - Multi-drone delivery coordination
/// - Collaborative search and rescue
/// - Agricultural monitoring with multiple UAVs
/// - Infrastructure inspection campaigns
///
/// TWO METHODS:
/// - classical (default): a dispatcher decides WHEN each task runs and WHICH
///   drone flies it, with the flight between tasks, payload, range, battery and
///   separation checked on the same flight model the evidence uses. A watch no
///   single drone can hold is flown in relief shifts sized to each drone.
/// - quantum: the library's QAOA scheduler (TaskScheduling) decides WHEN only;
///   it books amounts of named resources and has no notion of "one of these
///   drones". It needs tasks x time slots qubits, at least the longest
///   dependency chain in slots: 8 tasks with a 5-task chain need 40 qubits,
///   beyond the local simulator, and it says so rather than sampling in vain.
///
/// 1:N PERMISSION EVIDENCE: deconfliction, endurance, C2 (900 MHz by default,
/// --c2-band 2400 for the short-range case), altitude, losing a drone before
/// launch (re-dispatched; tasks nobody can fly are named for the pilot), any
/// drone dropping out mid-flight (fallback on its own layer to its own pad,
/// leftovers re-dispatched) and the supervisor workload.
namespace FSharp.Azure.Quantum.Examples.Drones.SwarmTaskAllocation

open System
open System.Diagnostics
open System.Globalization
open System.IO

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.TaskScheduling.Types

open FSharp.Azure.Quantum.Examples.Common

module Mav = FSharp.Azure.Quantum.Examples.Drones.MavlinkMission
open FSharp.Azure.Quantum.Examples.Drones.Domain

// =============================================================================
// DOMAIN TYPES
// =============================================================================

/// Type of drone task
type DroneTaskType =
    | Takeoff
    | Delivery
    | Inspection
    | Surveillance
    | EmergencyResponse
    | RelaySetup
    | ReturnToBase
    | Charging

/// A task to be performed by a drone
type DroneTask =
    {
        Id: string
        TaskType: DroneTaskType
        WaypointId: string
        DurationMinutes: float
        Priority: int
        PayloadKg: float
        DependsOn: string list
    }

/// A drone resource
type DroneResource =
    {
        Id: string
        Model: string
        MaxRangeKm: float
        MaxPayloadKg: float
        BatteryCapacityWh: float
        /// Optional in drones.csv; only the permission evidence needs it (to fly
        /// the schedule's legs), the scheduler does not.
        CruiseSpeedMs: float option
    }

/// A point from waypoints.csv. altitude_m is read as the flight altitude above
/// ground at that point: the altitude-ceiling evidence depends on it.
type Waypoint =
    {
        Id: string
        Name: string
        Latitude: float
        Longitude: float
        AltitudeM: float
    }

// =============================================================================
// DATA PARSING
// =============================================================================

module Parse =
    let private tryGet (k: string) (row: Data.CsvRow) =
        row.Values |> Map.tryFind k |> Option.map (fun s -> s.Trim())

    /// The CSVs use '.' decimals whatever the machine's locale.
    let private tryFloat (s: string option) =
        match s with
        | None -> None
        | Some v when String.IsNullOrWhiteSpace v -> None
        | Some v ->
            match Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture) with
            | true, x -> Some x
            | false, _ -> None

    let private tryInt (s: string option) =
        match s with
        | None -> None
        | Some v when String.IsNullOrWhiteSpace v -> None
        | Some v ->
            match Int32.TryParse v with
            | true, x -> Some x
            | false, _ -> None

    let private parseTaskType (s: string) : DroneTaskType option =
        match s.ToLowerInvariant().Replace("_", "") with
        | "takeoff" -> Some Takeoff
        | "delivery" -> Some Delivery
        | "inspection" -> Some Inspection
        | "surveillance" -> Some Surveillance
        | "emergencyresponse" -> Some EmergencyResponse
        | "relaysetup" -> Some RelaySetup
        | "returntobase" -> Some ReturnToBase
        | "charging" -> Some Charging
        | _ -> None

    let readTasks (path: string) : DroneTask list * string list =
        let rows, structuralErrors = Data.readCsvWithHeaderWithErrors path

        let tasks, errors =
            rows
            |> List.mapi (fun i row ->
                let rowNum = i + 2

                match
                    tryGet "task_id" row,
                    tryGet "type" row,
                    tryGet "waypoint_id" row,
                    tryFloat (tryGet "duration_min" row),
                    tryInt (tryGet "priority" row),
                    tryFloat (tryGet "payload_kg" row)
                with
                | Some id, Some typeStr, Some wp, Some dur, Some pri, Some payload ->
                    match parseTaskType typeStr with
                    | Some taskType ->
                        let deps =
                            tryGet "depends_on" row
                            |> Option.map (fun s ->
                                s.Split([| ';'; ',' |], StringSplitOptions.RemoveEmptyEntries)
                                |> Array.map (fun x -> x.Trim())
                                |> Array.toList)
                            |> Option.defaultValue []

                        Ok
                            {
                                Id = id
                                TaskType = taskType
                                WaypointId = wp
                                DurationMinutes = dur
                                Priority = pri
                                PayloadKg = payload
                                DependsOn = deps
                            }
                    | None -> Error $"row=%d{rowNum} invalid task type '%s{typeStr}'"
                | _ -> Error $"row=%d{rowNum} missing or invalid task fields")
            |> List.mapi (fun i r -> (i + 2, r))
            // A task id names one task: dependencies and the schedule refer to
            // it, so a second row with the same id is rejected, not merged.
            |> List.fold
                (fun (oks: DroneTask list, errs, seen: Map<string, int>) (rowNum, r) ->
                    match r with
                    | Ok v ->
                        match seen.TryFind v.Id with
                        | Some first ->
                            (oks,
                             $"row=%d{rowNum} duplicate task_id '%s{v.Id}' (first on row %d{first}); row ignored"
                             :: errs,
                             seen)
                        | None -> (v :: oks, errs, seen.Add(v.Id, rowNum))
                    | Error e -> (oks, e :: errs, seen))
                ([], [], Map.empty)
            |> fun (oks, errs, _) -> (oks, errs)

        (List.rev tasks, structuralErrors @ (List.rev errors))

    let readDrones (path: string) : DroneResource list * string list =
        let rows, structuralErrors = Data.readCsvWithHeaderWithErrors path

        let drones, errors =
            rows
            |> List.mapi (fun i row ->
                let rowNum = i + 2

                match
                    tryGet "drone_id" row,
                    tryGet "model" row,
                    tryFloat (tryGet "max_range_km" row),
                    tryFloat (tryGet "max_payload_kg" row),
                    tryFloat (tryGet "battery_capacity_wh" row)
                with
                | Some id, Some model, Some range, Some payload, Some battery ->
                    Ok
                        {
                            Id = id
                            Model = model
                            MaxRangeKm = range
                            MaxPayloadKg = payload
                            BatteryCapacityWh = battery
                            CruiseSpeedMs = tryFloat (tryGet "cruise_speed_ms" row)
                        }
                | _ -> Error $"row=%d{rowNum} missing or invalid drone fields")
            |> List.fold
                (fun (oks, errs) r ->
                    match r with
                    | Ok v -> (v :: oks, errs)
                    | Error e -> (oks, e :: errs))
                ([], [])

        (List.rev drones, structuralErrors @ (List.rev errors))

    let readWaypoints (path: string) : Waypoint list * string list =
        if not (File.Exists path) then
            ([], [ $"waypoints file not found: %s{path}" ])
        else
            let rows, structuralErrors = Data.readCsvWithHeaderWithErrors path

            let waypoints, errors =
                rows
                |> List.mapi (fun i row ->
                    match
                        tryGet "waypoint_id" row,
                        tryFloat (tryGet "latitude" row),
                        tryFloat (tryGet "longitude" row),
                        tryFloat (tryGet "altitude_m" row)
                    with
                    | Some id, Some lat, Some lon, Some alt ->
                        Ok
                            {
                                Id = id
                                Name = tryGet "name" row |> Option.defaultValue id
                                Latitude = lat
                                Longitude = lon
                                AltitudeM = alt
                            }
                    | _ -> Error(sprintf "row=%d missing or invalid waypoint fields" (i + 2)))
                |> List.fold
                    (fun (oks, errs) r ->
                        match r with
                        | Ok v -> (v :: oks, errs)
                        | Error e -> (oks, e :: errs))
                    ([], [])

            (List.rev waypoints, structuralErrors @ (List.rev errors))

// =============================================================================
// TASK SCHEDULING CONVERSION
// =============================================================================

module Scheduler =

    /// A task's duration with the domain's overheads for take-off and landing.
    let effectiveDurationMin (task: DroneTask) =
        match task.TaskType with
        | Takeoff -> task.DurationMinutes + Scheduling.preflightCheckDurationMin
        | ReturnToBase -> task.DurationMinutes + Scheduling.takeoffLandingOverheadMin
        | Charging -> max task.DurationMinutes Scheduling.fastChargeTo80PercentMin
        | _ -> task.DurationMinutes

    /// Convert drone task to FSharp.Azure.Quantum ScheduledTask
    /// Adds overhead time for takeoff/landing tasks based on domain constants
    let toScheduledTask (task: DroneTask) : ScheduledTask<DroneTaskType> =
        let effectiveDuration = effectiveDurationMin task

        {
            Id = task.Id
            Value = Some task.TaskType
            Duration = TimeSpan.FromMinutes effectiveDuration
            // NOTE: this demo does not model hard time windows or deadlines, so
            // EarliestStart/Deadline are None and the objective is MinimizeMakespan
            // (see buildProblem). Consequently task PRIORITY influences only the
            // solver's ready-task ordering / tie-breaking (higher = scheduled
            // first), not deadline satisfaction, and emergency PREEMPTION is not
            // modelled. To make priority drive lateness, add per-task Deadlines and
            // switch the objective to MinimizeLateness.
            EarliestStart = None
            Deadline = None
            ResourceRequirements =
                if task.PayloadKg > 0.0 then
                    Map.ofList [ ("payload_capacity", task.PayloadKg) ]
                else
                    Map.empty
            // tasks.csv uses the SCHEDULER convention: higher number = more
            // important (emergency_response = 5, takeoff/return = 0), matching
            // ScheduledTask.Priority (higher = more important). This is the inverse
            // of DroneDomain.Scheduling's aviation ladder (1 = highest); use
            // DroneDomain.Scheduling.toSchedulerPriority when bridging from that.
            Priority = float task.Priority
            Properties = Map.ofList [ ("waypoint", task.WaypointId) ]
        }

    /// Convert drone to FSharp.Azure.Quantum Resource
    /// Uses domain constant for minimum ground time between operations
    let toResource (drone: DroneResource) : Resource<string> =
        {
            Id = drone.Id
            Value = Some drone.Model
            Capacity = drone.MaxPayloadKg
            AvailableWindows = [ (0.0, 1440.0) ] // Available all day (in minutes)
            CostPerUnit = Scheduling.minGroundTimeMin // Minimum turnaround time as "cost"
            Properties =
                Map.ofList
                    [
                        ("max_range_km", string drone.MaxRangeKm)
                        ("battery_wh", string drone.BatteryCapacityWh)
                    ]
        }

    /// Create dependency from task reference (using FinishToStart DU)
    let toDependency (fromTaskId: string) (toTaskId: string) : Dependency =
        FinishToStart(fromTaskId, toTaskId, TimeSpan.Zero) // lag = 0 means tasks can start immediately after predecessor

    /// Build scheduling problem from drone tasks and resources
    let buildProblem (tasks: DroneTask list) (drones: DroneResource list) : SchedulingProblem<DroneTaskType, string> =
        let scheduledTasks = tasks |> List.map toScheduledTask
        let resources = drones |> List.map toResource

        // Extract dependencies from task definitions
        let dependencies =
            tasks
            |> List.collect (fun task -> task.DependsOn |> List.map (fun depId -> toDependency depId task.Id))

        // Calculate time horizon based on total task duration + buffer
        let totalDuration = tasks |> List.sumBy (fun t -> t.DurationMinutes)
        let timeHorizon = TimeSpan.FromMinutes(totalDuration * 2.0) // 2x buffer for scheduling flexibility

        {
            Tasks = scheduledTasks
            Resources = resources
            Dependencies = dependencies
            Objective = MinimizeMakespan
            TimeHorizon = timeHorizon
        }

    /// Solve scheduling problem with quantum backend
    let solveWithQuantum
        (backend: IQuantumBackend)
        (problem: SchedulingProblem<DroneTaskType, string>)
        : Async<QuantumResult<Solution>> =
        solveQuantum backend problem

// =============================================================================
// VISUALIZATION
// =============================================================================

module Visualization =

    /// Generate ASCII Gantt chart
    let generateGanttChart (solution: Solution) (timeScale: float) : string =
        let sb = System.Text.StringBuilder()

        sb.AppendLine("") |> ignore

        sb.AppendLine "╔════════════════════════════════════════════════════════════════════════════╗"
        |> ignore

        sb.AppendLine "║  TASK SCHEDULE GANTT CHART                                                 ║"
        |> ignore

        sb.AppendLine "╠════════════════════════════════════════════════════════════════════════════╣"
        |> ignore

        // Time axis
        let maxTime = solution.Makespan.TotalMinutes
        // At most 20 ticks: a longer schedule widens the tick (to whole 5 minutes)
        // rather than cutting off its later tasks.
        let timeScale = max timeScale (5.0 * Math.Ceiling(maxTime / 20.0 / 5.0))
        let numTicks = min 20 (int (Math.Ceiling(maxTime / timeScale)))
        let tickWidth = 3

        sb.Append("║ Task         |") |> ignore

        for i in 0..numTicks do
            sb.Append(sprintf "%3d" (int (float i * timeScale))) |> ignore

        sb.AppendLine(" (min)") |> ignore

        sb.Append("║ -------------|") |> ignore

        for _ in 0..numTicks do
            sb.Append("---") |> ignore

        sb.AppendLine("") |> ignore

        // Task bars
        for assignment in solution.Assignments |> List.sortBy (fun a -> a.StartTime) do
            let startPos = int (assignment.StartTime.TotalMinutes / timeScale)
            let endPos = int (assignment.EndTime.TotalMinutes / timeScale)
            let barLength = max 1 (endPos - startPos)

            let taskName =
                if assignment.TaskId.Length > 12 then
                    assignment.TaskId.Substring(0, 12)
                else
                    assignment.TaskId.PadRight 12

            sb.Append($"║ %s{taskName} |") |> ignore

            for i in 0..numTicks do
                if i >= startPos && i < startPos + barLength then
                    sb.Append("███") |> ignore
                else
                    sb.Append("   ") |> ignore

            sb.AppendLine("") |> ignore

        sb.AppendLine "╚════════════════════════════════════════════════════════════════════════════╝"
        |> ignore

        sb.ToString()

// =============================================================================
// METRICS
// =============================================================================

type Metrics =
    {
        run_id: string
        tasks_path: string
        drones_path: string
        tasks_sha256: string
        drones_sha256: string
        task_count: int
        drone_count: int
        dependency_count: int
        method_used: string
        makespan_min: float
        total_idle_time_min: float
        resource_utilization: float
        elapsed_ms: int64
    }

// =============================================================================
// 1:N PERMISSION EVIDENCE
// =============================================================================

/// Evidence for flying the produced schedule under a one-pilot-to-many permission.
///
/// The schedule gives each task a start and an end time but no geometry, so the
/// evidence first turns it into flights:
///   - WHERE: each task's waypoint from waypoints.csv, in local metres around
///     the base (the pilot station);
///   - WHO: the aircraft the schedule names for the task, if any; the solver's
///     assignments carry the task's resource REQUIREMENT key, not a drone id,
///     so otherwise a first-fit dispatch made here picks the aircraft, and the
///     pack says so;
///   - HOW: straight legs at the aircraft's cruise speed and a hover (a loiter
///     for fixed-wing) at the waypoint for the task's scheduled interval.
/// The schedule's times are taken as given: where they cannot be flown, that
/// is a finding, never something adjusted here.
module Evidence =

    module Ev = FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence
    module Dom = FSharp.Azure.Quantum.Examples.Drones.Domain

    [<Literal>]
    let private c2FadeMarginDb = 10.0

    [<Literal>]
    let private sampleStepS = 1.0

    [<Literal>]
    let private groundZ = 0.5

    let private usableFraction = 1.0 - Battery.reserveBatteryPercent / 100.0

    /// One scheduled task placed on the map.
    type Visit =
        {
            TaskId: string
            WaypointId: string
            Pos: Ev.P3
            StartS: float
            EndS: float
            PayloadKg: float
            DependsOn: string list
        }

    type Aircraft =
        {
            Drone: DroneResource
            SpeedMs: float
            /// Equivalent all-up mass for DroneDomain's power model: the mass at
            /// which that model flies the rated max_range_km on the full battery
            /// at cruise speed. drones.csv has no masses, and this ties the
            /// energy estimate to the aircraft's own rating instead of a guess.
            MassKg: float
            /// A fixed-wing cannot hover: it loiters at cruise power.
            FixedWing: bool
            /// Its own launch and landing pad at the base. One shared pad puts a
            /// landing aircraft onto one taking off, and a fallback descending
            /// onto the base through anything hovering above it.
            Pad: Ev.P3
        }

    type Sortie =
        {
            Aircraft: Aircraft
            Number: int
            Visits: Visit list
            /// The sampled track, built only when read: the dispatcher tries
            /// many sorties whose timing already rules them out.
            LazySamples: Lazy<(float * Ev.P3)[]>
            /// MISSION_START and touchdown: the track's first and last sample times.
            LaunchS: float
            LandS: float
            /// Payload of all the sortie's tasks, carried from launch.
            PayloadKg: float
            DistanceKm: float
            EnergyWh: float
            /// (task, seconds) where the aircraft reaches the task after its start.
            Late: (string * float) list
            /// (arrival, departure) at each visit, in visit order.
            Stays: (float * float) list
        }

        /// Where the aircraft is, when (a straight line between samples).
        member s.Samples = s.LazySamples.Value

    /// One way of flying the schedule with a given fleet.
    type Outcome =
        {
            Sorties: Sortie list
            Tracks: Ev.Track list
            /// Task -> aircraft id.
            Assignment: Map<string, string>
            Unassigned: (string * string) list
        }

    let private baseGround: Ev.P3 = { X = 0.0; Y = 0.0; Z = 0.0 }

    /// Equirectangular projection around the base. Over a few kilometres the
    /// error is centimetres, far below the separation limit.
    let project (origin: Waypoint) (w: Waypoint) : Ev.P3 =
        let r = Dom.Environment.earthRadiusKm * 1000.0
        let rad (deg: float) = deg * Math.PI / 180.0

        {
            X = rad (w.Longitude - origin.Longitude) * r * Math.Cos(rad origin.Latitude)
            Y = rad (w.Latitude - origin.Latitude) * r
            Z = w.AltitudeM
        }

    let aircraftOf (d: DroneResource) : Aircraft option =
        d.CruiseSpeedMs
        |> Option.filter (fun v -> v > 0.0 && d.MaxRangeKm > 0.0 && d.BatteryCapacityWh > 0.0)
        |> Option.map (fun v ->
            // Rated range at cruise speed on the full battery -> cruise power.
            let cruiseW = d.BatteryCapacityWh * v * 3.6 / d.MaxRangeKm

            {
                Drone = d
                SpeedMs = v
                MassKg = cruiseW / (Battery.hoverPowerWPerKg * Battery.forwardFlightEfficiencyFactor)
                FixedWing = d.Model.StartsWith("FixedWing", StringComparison.OrdinalIgnoreCase)
                Pad = baseGround
            })

    /// The flyable fleet, each aircraft on its own pad: a ring around the base
    /// wide enough that neighbouring pads, and the column above the base, are
    /// all at least two minimum separations apart.
    let fleetOf (drones: DroneResource list) : Aircraft list =
        let fleet = drones |> List.choose aircraftOf
        let n = max 1 fleet.Length
        let spacing = 2.0 * Safety.minSwarmSeparationMeters
        let radius = max spacing (spacing / (2.0 * Math.Sin(Math.PI / float (max 2 n))))

        fleet
        |> List.mapi (fun i a ->
            let angle = 2.0 * Math.PI * float i / float n

            { a with
                Pad =
                    {
                        X = radius * Math.Cos angle
                        Y = radius * Math.Sin angle
                        Z = 0.0
                    }
            })

    /// The autopilot the aircraft flies with: fixed-wing models are QuadPlanes
    /// (vertical take-off and landing on their pads), the rest copters.
    let vehicleOf (a: Aircraft) =
        if a.FixedWing then Mav.ArduQuadPlane else Mav.ArduCopter

    let private horizontalM (p: Ev.P3) (q: Ev.P3) =
        Math.Sqrt((q.X - p.X) ** 2.0 + (q.Y - p.Y) ** 2.0)

    /// Straight above the pad at `p`'s altitude: aircraft climb and land
    /// vertically on their own pads.
    let private overPad (a: Aircraft) (p: Ev.P3) = { a.Pad with Z = p.Z }

    /// Seconds of a straight leg as the aircraft flies it (Mav.Flight.legS: a
    /// copter from rest to rest, a fixed-wing at cruise).
    let private flyS (a: Aircraft) (p: Ev.P3) (q: Ev.P3) =
        Mav.Flight.legS (vehicleOf a) a.SpeedMs (horizontalM p q) (q.Z - p.Z)

    /// A copter settles at every waypoint it flies to (Mav.stopS: reaching it, then the hold), as the
    /// exported waypoints make it.
    let private settleS (a: Aircraft) = if a.FixedWing then 0.0 else Mav.stopS

    /// Seconds from leaving `p` to having come to rest at the waypoint `q`.
    let private travelS (a: Aircraft) (p: Ev.P3) (q: Ev.P3) = flyS a p q + settleS a

    /// From MISSION_START on the pad to arriving at `p`: spool-up, the
    /// vertical climb over the pad to p's altitude, the leg to p.
    let private outS (a: Aircraft) (p: Ev.P3) =
        Mav.Flight.spoolUpS + flyS a a.Pad (overPad a p) + travelS a (overPad a p) p

    /// From leaving `p` to being over the pad at p's altitude, ready to land: a
    /// fixed-wing slows on its way into the VTOL landing.
    let private homeS (a: Aircraft) (p: Ev.P3) =
        travelS a p (overPad a p)
        + (if a.FixedWing then Mav.Flight.vtolApproachS else 0.0)

    /// From leaving `p` to touching down on the pad: the leg back over the pad
    /// at p's altitude, then the landing.
    let private backS (a: Aircraft) (p: Ev.P3) =
        homeS a p + Mav.Flight.landingS (vehicleOf a) p.Z

    /// Samples of the leg from `p` to `q` starting at `t`, arrival included:
    /// through a copter's (or a vertical leg's) S-curve ramps; a fixed-wing
    /// cruise leg is straight at constant speed.
    let private legSamples (a: Aircraft) (t: float) (p: Ev.P3) (q: Ev.P3) =
        let h = horizontalM p q
        let dz = q.Z - p.Z
        let v = Mav.Flight.vertical (vehicleOf a)

        let lerp (f: float) : Ev.P3 =
            {
                X = p.X + f * (q.X - p.X)
                Y = p.Y + f * (q.Y - p.Y)
                Z = p.Z + f * (q.Z - p.Z)
            }

        let ramps =
            if a.FixedWing && h >= 1.0 then
                []
            else
                Mav.Flight.copterLeg a.SpeedMs v.ClimbMs v.DescentMs h dz
                |> Mav.Flight.legFractions
                |> List.map (fun (dt, f) -> (t + dt, lerp f))

        let arrive = t + flyS a p q
        ramps @ [ (arrive, q); (arrive + settleS a, q) ]

    /// A fixed-wing cannot hover: over a task it circles the point at its
    /// turn radius (WP_LOITER_RAD in the exported parameters), sixteen samples
    /// a turn. A copter holds still.
    let private holdSamples (a: Aircraft) (fromS: float) (toS: float) (p: Ev.P3) =
        if toS <= fromS then
            []
        elif a.FixedWing then
            let r = Mav.Flight.turnRadiusM a.SpeedMs
            let stepS = 2.0 * Math.PI * r / a.SpeedMs / 16.0
            let n = max 1 (int (Math.Ceiling((toS - fromS) / stepS)))

            [
                for k in 1..n ->
                    let t = min toS (fromS + float k * stepS)
                    // Clockwise, entering the circle from its centre's south.
                    let angle = -Math.PI / 2.0 - a.SpeedMs * (t - fromS) / r

                    (t,
                     { p with
                         X = p.X + r * Math.Cos angle
                         Y = p.Y + r * Math.Sin angle
                     })
            ]
        else
            [ (toS, p) ]

    /// Energy of one straight segment between two samples, from DroneDomain's
    /// power model (hover power per kg, forward-flight and climb factors).
    let private segmentWh (a: Aircraft) (payloadKg: float) (t0: float, p0: Ev.P3) (t1: float, p1: Ev.P3) =
        let dt = t1 - t0

        if dt <= 0.0 then
            0.0
        else
            let horizontal = Math.Sqrt((p1.X - p0.X) ** 2.0 + (p1.Y - p0.Y) ** 2.0)
            let moving = horizontal > 1.0 || abs (p1.Z - p0.Z) > 1.0

            let speed =
                if moving then horizontal / dt
                elif a.FixedWing then a.SpeedMs
                else 0.0

            let climb = if moving then (p1.Z - p0.Z) / dt else 0.0
            estimatePowerConsumption (a.MassKg + payloadKg) speed climb * dt / 3600.0

    /// Fly one sortie as the exported mission does: launch (MISSION_START) on
    /// the aircraft's pad so as to reach the first task at its start, spool up
    /// and climb vertically, hover over each task's interval (a fixed-wing
    /// circles), fly straight on to the next task (waiting there if early,
    /// recording it if late), and back over the pad and down onto it.
    let private sortie (a: Aircraft) (number: int) (visits: Visit list) : Sortie =
        let first = List.head visits
        let launch = first.StartS - outS a first.Pos
        let climbFrom = launch + Mav.Flight.spoolUpS
        let top = overPad a first.Pos
        // NAV_TAKEOFF: the climb ends without a hold, so no settle sample.
        let atTop = climbFrom + flyS a a.Pad top

        // `path`: the waypoints and hold ends (distance and energy); `legs`:
        // each leg's start and its task's arrival and departure, from which
        // `track` (with the S-curve ramps and orbits) is built when needed.
        let path, legs, clock, pos, late =
            visits
            |> List.fold
                (fun (path, legs, clock, pos, late) (v: Visit) ->
                    let arrive = clock + travelS a pos v.Pos
                    let leave = max arrive v.EndS

                    let late =
                        if arrive > v.StartS + 1e-6 then
                            (v.TaskId, arrive - v.StartS) :: late
                        else
                            late

                    (path @ [ (arrive, v.Pos); (leave, v.Pos) ],
                     (clock, pos, arrive, leave, v.Pos) :: legs,
                     leave,
                     v.Pos,
                     late))
                ([ (launch, a.Pad); (climbFrom, a.Pad); (atTop, top) ], [], atTop, top, [])

        let above = overPad a pos
        let overAt = clock + homeS a pos
        let down = overAt + Mav.Flight.landingS (vehicleOf a) pos.Z
        let path = path @ [ (overAt, above); (down, a.Pad) ] |> Array.ofList

        let track =
            lazy
                (let climb = legSamples a climbFrom a.Pad top

                 // A fixed-wing's leg home is one straight line, flown slower
                 // for the approach; a copter's goes through its S-curve ramps.
                 let home =
                     if a.FixedWing then
                         [ (overAt, above) ]
                     else
                         legSamples a clock pos above

                 [
                     yield (launch, a.Pad)
                     yield (climbFrom, a.Pad)
                     yield! List.truncate (climb.Length - 1) climb
                     for start, from, arrive, leave, at in List.rev legs do
                         yield! legSamples a start from at
                         yield! holdSamples a arrive leave at
                     yield! home
                     yield (down, a.Pad)
                 ]
                 |> Array.ofList)

        let payload = visits |> List.sumBy (fun v -> v.PayloadKg)
        let pairs = path |> Array.pairwise

        {
            Aircraft = a
            Number = number
            Visits = visits
            LazySamples = track
            LaunchS = launch
            LandS = down
            PayloadKg = payload
            DistanceKm =
                pairs
                |> Array.sumBy (fun ((_, p), (_, q)) -> Ev.dist3 p q)
                |> fun m -> m / 1000.0
            EnergyWh = pairs |> Array.sumBy (fun (s0, s1) -> segmentWh a payload s0 s1)
            Late = List.rev late
            Stays =
                path
                |> Array.toList
                |> List.skip 3
                |> List.truncate (2 * visits.Length)
                |> List.chunkBySize 2
                |> List.map (fun xs -> (fst xs.[0], fst xs.[1]))
        }

    /// Split one aircraft's tasks into sorties: it lands on its pad between two
    /// tasks (fresh battery, minimum ground time, at least the launcher's
    /// turnaround) whenever the gap allows the round trip, and otherwise flies
    /// straight on.
    let private sorties (a: Aircraft) (visits: Visit list) : Sortie list =
        let groundS = max (Scheduling.minGroundTimeMin * 60.0) Mav.Flight.turnaroundS

        visits
        |> List.sortBy (fun v -> v.StartS)
        |> List.fold
            (fun groups (v: Visit) ->
                match groups with
                | (last :: _ as current) :: rest when last.EndS + backS a last.Pos + groundS + outS a v.Pos > v.StartS ->
                    (v :: current) :: rest
                | _ -> [ v ] :: groups)
            []
        |> List.rev
        |> List.mapi (fun i vs -> sortie a (i + 1) (List.rev vs))

    let label (s: Sortie) =
        $"%s{s.Aircraft.Drone.Id} #%d{s.Number}"

    /// The circles fixed-wings fly over their tasks: (who, centre, radius, from,
    /// until). Where on its circle a fixed-wing is cannot be planned (it joins
    /// the circle wherever it arrives), so the whole circle counts as flown.
    let circles (ss: Sortie list) =
        [
            for s in ss do
                if s.Aircraft.FixedWing then
                    let radius = Mav.Flight.turnRadiusM s.Aircraft.SpeedMs

                    for v, (arrive, leave) in List.zip s.Visits s.Stays do
                        if leave > arrive then
                            yield (label s, v.Pos, radius, arrive, leave)
        ]

    /// The closest `track` comes, while airborne, to any of `circles` while it is
    /// flown: the horizontal gap to the circle's rim and the height difference.
    let private circleClearanceAt (circles: (string * Ev.P3 * float * float * float) list) (track: Ev.Track) =
        let first =
            if track.Samples.Length = 0 then
                0.0
            else
                fst track.Samples.[0]

        let last =
            if track.Samples.Length = 0 then
                -1.0
            else
                fst track.Samples.[track.Samples.Length - 1]

        [
            for who, centre, radius, arrive, leave in circles do
                if who <> track.AircraftId && arrive <= last && leave >= first then
                    for t in max arrive first .. sampleStepS .. min leave last do
                        match Ev.positionAt track t with
                        | Some p when p.Z > groundZ ->
                            let rim = Math.Sqrt((p.X - centre.X) ** 2.0 + (p.Y - centre.Y) ** 2.0) - radius
                            yield (Math.Sqrt(rim * rim + (p.Z - centre.Z) ** 2.0), t, who)
                        | _ -> ()
        ]
        |> List.sortBy (fun (d, _, _) -> d)
        |> List.tryHead

    let private circleClearance circles (track: Ev.Track) =
        circleClearanceAt circles track
        |> Option.map (fun (d, _, _) -> d)
        |> Option.defaultValue Double.PositiveInfinity

    let private describe (s: Sortie) =
        sprintf "%s (%s)" (label s) (s.Visits |> List.map (fun v -> v.TaskId) |> String.concat ", ")

    /// Give every task an aircraft, in start-time order. A task the schedule
    /// assigns to a (still available) aircraft keeps it. Otherwise, among the
    /// aircraft that can carry its payload and reach it by its start: one that
    /// flew a predecessor of the task, else the first in drones.csv order. If
    /// none can reach it in time, the one that gets there first (reported late).
    let fly (fleet: Aircraft list) (named: Map<string, string>) (visits: Visit list) : Outcome =
        let assigned, unassigned =
            visits
            |> List.sortBy (fun v -> (v.StartS, v.TaskId))
            |> List.fold
                (fun (plan: Map<string, Visit list>, unassigned) (v: Visit) ->
                    let flown (a: Aircraft) =
                        plan.TryFind a.Drone.Id |> Option.defaultValue []

                    let lateness (a: Aircraft) =
                        match flown a with
                        | last :: _ -> last.EndS + travelS a last.Pos v.Pos - v.StartS
                        | [] -> Double.NegativeInfinity

                    let capable =
                        fleet |> List.filter (fun a -> v.PayloadKg <= a.Drone.MaxPayloadKg + 1e-9)

                    let chosen =
                        match
                            named.TryFind v.TaskId
                            |> Option.bind (fun id -> fleet |> List.tryFind (fun a -> a.Drone.Id = id))
                        with
                        | Some a -> Some a
                        | None ->
                            match capable |> List.filter (fun a -> lateness a <= 1e-9) with
                            | [] when capable.IsEmpty -> None
                            | [] -> Some(capable |> List.minBy lateness)
                            | onTime ->
                                onTime
                                |> List.tryFind (fun a ->
                                    flown a |> List.exists (fun p -> List.contains p.TaskId v.DependsOn))
                                |> Option.orElse (Some onTime.Head)

                    match chosen with
                    | Some a -> (plan.Add(a.Drone.Id, v :: flown a), unassigned)
                    | None ->
                        let heaviest =
                            fleet |> List.map (fun a -> a.Drone.MaxPayloadKg) |> List.fold max 0.0

                        (plan,
                         (v.TaskId,
                          sprintf
                              "%s: payload %.1f kg exceeds every aircraft (max %.1f kg)"
                              v.TaskId
                              v.PayloadKg
                              heaviest)
                         :: unassigned))
                (Map.empty, [])

        let flights =
            fleet
            // Kept newest-first while dispatching; fly them in dispatch order.
            |> List.collect (fun a ->
                assigned.TryFind a.Drone.Id
                |> Option.map (List.rev >> sorties a)
                |> Option.defaultValue [])

        {
            Sorties = flights
            Tracks =
                flights
                |> List.map (fun s ->
                    {
                        AircraftId = label s
                        Samples = s.Samples
                    }
                    : Ev.Track)
            Assignment =
                assigned
                |> Map.toList
                |> List.collect (fun (id, vs) -> vs |> List.map (fun v -> (v.TaskId, id)))
                |> Map.ofList
            Unassigned = List.rev unassigned
        }

    let private rangeLeg (s: Sortie) =
        (describe s, s.DistanceKm, s.Aircraft.Drone.MaxRangeKm * usableFraction)

    /// Airborne minutes vs. minutes the battery gives above reserve at the
    /// sortie's mean power (DroneDomain.estimateRemainingFlightTime).
    let private timeLeg (s: Sortie) =
        let airS = s.LandS - s.LaunchS
        let meanW = if airS > 0.0 then s.EnergyWh * 3600.0 / airS else 0.0
        (describe s, airS / 60.0, estimateRemainingFlightTime s.Aircraft.Drone.BatteryCapacityWh 100.0 meanW)

    let private short legs =
        legs |> List.filter (fun (_, need, have) -> need > have)

    let private overweight (o: Outcome) =
        o.Sorties
        |> List.filter (fun s -> s.PayloadKg > s.Aircraft.Drone.MaxPayloadKg + 1e-9)

    let private lateArrivals (o: Outcome) =
        o.Sorties
        |> List.collect (fun s -> s.Late |> List.map (fun (t, dt) -> (s, t, dt)))

    /// Every pair of flights that comes closer than the limit, closest first.
    let private closePairs (tracks: Ev.Track list) =
        [
            for i, a in List.indexed tracks do
                for b in List.skip (i + 1) tracks do
                    match Ev.closestApproach sampleStepS groundZ [ a; b ] with
                    | Some c when c.Distance < Safety.minSwarmSeparationMeters -> yield c
                    | _ -> ()
        ]
        |> List.sortBy (fun c -> c.Distance)

    /// The task a visit belongs to: relief shifts are "T006#1", "T006#2", ...
    let taskOf (visitId: string) =
        match visitId.IndexOf '#' with
        | -1 -> visitId
        | i -> visitId.Substring(0, i)

    /// The classical dispatcher: decides WHEN each task runs and WHICH aircraft
    /// flies it, planned with the same flight model the checks below use, so
    /// the evidence cannot disagree with the plan. The library scheduler cannot
    /// do this part: it books amounts of named resources, and has no notion of
    /// "one of these drones" or of the flight between two tasks.
    ///
    /// Tasks go in dependency order; among the tasks ready to place, the one
    /// with the smallest `readyKey` goes first. Each goes to the capable
    /// aircraft that can start it earliest while every one of that aircraft's
    /// sorties stays flyable (reached on time, within payload, range and
    /// battery) and clear of every other aircraft's tracks, waiting in steps of
    /// `waitStepS` when it must. `fixedPlan` and `doneAt` let the same function
    /// re-dispatch the leftovers of an aircraft that dropped out.
    let dispatchBy
        (readyKey: DroneTask -> float * float * string)
        (fleet: Aircraft list)
        (work: (DroneTask * Visit) list)
        (fixedPlan: Map<string, Visit list>)
        (doneAt: Map<string, float>)
        (notBefore: float)
        : Map<string, Visit list> * (string * string) list =
        let waitStepS = 30.0
        // How far past its earliest start a task may be pushed. tasks.csv has no
        // deadlines, so this bounds the search, not the mission.
        let maxWaitS = 4.0 * 3600.0

        let flyable (a: Aircraft) (vs: Visit list) =
            let ss = sorties a vs

            let ok =
                ss
                |> List.forall (fun s ->
                    let _, needKm, haveKm = rangeLeg s
                    let _, needMin, haveMin = timeLeg s

                    s.Late.IsEmpty
                    && s.PayloadKg <= a.Drone.MaxPayloadKg + 1e-9
                    && needKm <= haveKm
                    && needMin <= haveMin)

            (ok, ss)

        let tracksOf (plan: Map<string, Visit list>) (except: string) =
            fleet
            |> List.filter (fun a -> a.Drone.Id <> except)
            |> List.collect (fun a -> plan.TryFind a.Drone.Id |> Option.map (sorties a) |> Option.defaultValue [])

        let trackOf (s: Sortie) : Ev.Track =
            {
                AircraftId = label s
                Samples = s.Samples
            }

        // Clear of every other flight, and of every circle a fixed-wing flies
        // over a task, anywhere on that circle (see `circles`).
        let clear (ss: Sortie list) (others: Sortie list) =
            let otherTracks = others |> List.map trackOf
            let mine = ss |> List.map trackOf

            mine
            |> List.forall (fun m ->
                otherTracks
                |> List.forall (fun o ->
                    match Ev.closestApproach sampleStepS groundZ [ m; o ] with
                    | Some c -> c.Distance >= Safety.minSwarmSeparationMeters
                    | None -> true))
            && mine
               |> List.forall (fun m -> circleClearance (circles others) m >= Safety.minSwarmSeparationMeters)
            && otherTracks
               |> List.forall (fun o -> circleClearance (circles ss) o >= Safety.minSwarmSeparationMeters)

        /// Place one visit: at the earliest workable start (waiting in steps),
        /// or, for a relief shift, exactly at `exact` or not at all.
        let place
            (plan: Map<string, Visit list>)
            (doneAt: Map<string, float>)
            (exact: float option)
            (t: DroneTask, v: Visit)
            =
            let duration = v.EndS - v.StartS

            let depsEnd =
                t.DependsOn |> List.map (fun d -> doneAt.[d]) |> List.fold max notBefore

            fleet
            |> List.filter (fun a -> v.PayloadKg <= a.Drone.MaxPayloadKg + 1e-9)
            |> List.choose (fun a ->
                let others = tracksOf plan a.Drone.Id
                let mine = plan.TryFind a.Drone.Id |> Option.defaultValue []
                // Never launch before `notBefore`.
                let earliest = max depsEnd (notBefore + outS a v.Pos)

                let starts =
                    match exact with
                    | Some s0 when s0 + 1e-6 >= earliest -> [ s0 ]
                    | Some _ -> []
                    | None -> [ 0.0 .. waitStepS .. maxWaitS ] |> List.map (fun w -> earliest + w)

                starts
                |> List.tryPick (fun start ->

                    let candidate =
                        { v with
                            StartS = start
                            EndS = start + duration
                        }

                    let ok, ss = flyable a (mine @ [ candidate ])

                    if ok && clear ss others then
                        Some(start, a, candidate)
                    else
                        None))
            |> List.sortBy (fun (start, _, _) -> start)
            |> List.tryHead

        /// A watch longer than any one aircraft can hold is flown in relief
        /// shifts. Each shift is as long as the arriving aircraft can hold
        /// (a heavy lifter takes a long one, a small quad a short one); the next
        /// aircraft arrives exactly as the previous one leaves, on a station two
        /// minimum separations to the side of the line from the base and as
        /// much higher (lower under the ceiling), so a handover never puts two
        /// aircraft on one point, a copter arriving never crosses a fixed-wing
        /// still circling, and the watch is never left empty.
        let placeShifts (plan: Map<string, Visit list>) (doneAt: Map<string, float>) (t: DroneTask, v: Visit) =
            let relief = 2.0 * Safety.minSwarmSeparationMeters
            let minShiftS = 120.0
            let maxShifts = 16

            let depsEnd =
                t.DependsOn |> List.map (fun d -> doneAt.[d]) |> List.fold max notBefore

            let station k =
                if k % 2 = 1 then
                    let r = Math.Sqrt(v.Pos.X * v.Pos.X + v.Pos.Y * v.Pos.Y)
                    let nx, ny = if r > 1e-6 then (-v.Pos.Y / r, v.Pos.X / r) else (1.0, 0.0)

                    let z =
                        if v.Pos.Z + relief <= Regulations.maxAltitudeAglMeters then
                            v.Pos.Z + relief
                        else
                            v.Pos.Z - relief

                    { v.Pos with
                        X = v.Pos.X + relief * nx
                        Y = v.Pos.Y + relief * ny
                        Z = z
                    }
                else
                    v.Pos

            // The longest stay from `start` (up to `upTo`) that keeps the
            // aircraft's sorties flyable and clear of everyone else.
            let longestStay (plan: Map<string, Visit list>) (a: Aircraft) (shift: Visit) (start: float) (upTo: float) =
                let mine = plan.TryFind a.Drone.Id |> Option.defaultValue []
                let others = tracksOf plan a.Drone.Id

                let fits d =
                    let ok, ss =
                        flyable
                            a
                            (mine
                             @ [
                                 { shift with
                                     StartS = start
                                     EndS = start + d
                                 }
                             ])

                    ok && clear ss others

                if start + 1e-6 < max depsEnd (notBefore + outS a shift.Pos) then
                    None
                elif fits upTo then
                    Some upTo
                elif not (fits (min upTo minShiftS)) then
                    None
                else
                    let rec search lo hi n =
                        if n = 0 then
                            lo
                        else
                            let mid = (lo + hi) / 2.0

                            if fits mid then
                                search mid hi (n - 1)
                            else
                                search lo mid (n - 1)

                    Some(search (min upTo minShiftS) upTo 12)

            let capable =
                fleet |> List.filter (fun a -> v.PayloadKg <= a.Drone.MaxPayloadKg + 1e-9)

            let rec go (plan: Map<string, Visit list>) k (start: float option) (remaining: float) acc =
                if remaining <= 1e-6 then
                    Some(plan, List.rev acc)
                elif k >= maxShifts then
                    None
                else
                    let shift =
                        { v with
                            TaskId = sprintf "%s#%d" t.Id (k + 1)
                            Pos = station k
                        }

                    let offers (s0: float) =
                        capable
                        |> List.choose (fun a ->
                            longestStay plan a shift s0 remaining |> Option.map (fun d -> (s0, a, d)))

                    let options =
                        match start with
                        | Some s0 -> offers s0
                        | None ->
                            [ 0.0 .. waitStepS .. maxWaitS ]
                            |> List.tryPick (fun w ->
                                match offers (depsEnd + w) with
                                | [] -> None
                                | xs -> Some xs)
                            |> Option.defaultValue []

                    match options |> List.sortByDescending (fun (_, _, d) -> d) |> List.tryHead with
                    | Some(s0, a, d) ->
                        let placed =
                            { shift with
                                StartS = s0
                                EndS = s0 + d
                            }

                        let mine = plan.TryFind a.Drone.Id |> Option.defaultValue []

                        go
                            (plan.Add(a.Drone.Id, mine @ [ placed ]))
                            (k + 1)
                            (Some(s0 + d))
                            (remaining - d)
                            (placed :: acc)
                    | None -> None

            go plan 0 None (v.EndS - v.StartS) []

        let latest (m: Map<string, float>) (k: string) (e: float) =
            max e (m.TryFind k |> Option.defaultValue e)

        // A task is done when its last piece ends. The fixed plan's visits are
        // keyed by visit id ("T006#1", "T006#2" for relief shifts), and a
        // dependent waits for the task, not for one of its shifts.
        let fixedEnd =
            fixedPlan
            |> Map.toList
            |> List.collect (fun (_, vs) -> vs |> List.map (fun v -> (taskOf v.TaskId, v.EndS)))
            |> List.fold (fun (m: Map<string, float>) (k, e) -> m.Add(k, latest m k e)) Map.empty

        /// The re-dispatched piece of a task ends it no earlier than its pieces
        /// the fixed plan still flies.
        let finish (doneAt: Map<string, float>) (id: string) (endS: float) = doneAt.Add(id, latest fixedEnd id endS)

        let rec loop (plan: Map<string, Visit list>) (doneAt: Map<string, float>) pending unassigned =
            let blocked, rest =
                pending
                |> List.partition (fun (t: DroneTask, _) ->
                    t.DependsOn
                    |> List.exists (fun d -> unassigned |> List.exists (fun (id, _) -> id = d)))

            let unassigned =
                unassigned
                @ (blocked
                   |> List.map (fun (t, _) -> (t.Id, $"%s{t.Id}: depends on a task nobody can fly")))

            let ready =
                rest
                |> List.filter (fun (t: DroneTask, _) -> t.DependsOn |> List.forall doneAt.ContainsKey)
                |> List.sortBy (fun (t, _) -> readyKey t)

            match ready with
            | [] ->
                (plan,
                 unassigned
                 @ (rest
                    |> List.map (fun (t, _) -> (t.Id, $"%s{t.Id}: its dependencies never complete"))))
            | (t, v) :: _ ->
                let rest = rest |> List.filter (fun (x, _) -> x.Id <> t.Id)

                match place plan doneAt None (t, v) with
                | Some(_, a, placed) ->
                    let mine = plan.TryFind a.Drone.Id |> Option.defaultValue []
                    loop (plan.Add(a.Drone.Id, mine @ [ placed ])) (finish doneAt t.Id placed.EndS) rest unassigned
                | None ->
                    match placeShifts plan doneAt (t, v) with
                    | Some(plan, shifts) -> loop plan (finish doneAt t.Id (List.last shifts).EndS) rest unassigned
                    | None ->
                        loop
                            plan
                            doneAt
                            rest
                            (unassigned
                             @ [
                                 (t.Id,
                                  sprintf
                                      "%s: no aircraft can fly it within %.0f min of its earliest start, whole or in relief shifts (payload, range, battery or separation)"
                                      t.Id
                                      (maxWaitS / 60.0))
                             ])

        // A task with a piece still to dispatch is not done yet, whatever of it
        // the fixed plan or `doneAt` already holds; `finish` records it once
        // that piece is placed.
        let pendingIds = work |> List.map (fun (t, _) -> t.Id) |> Set.ofList

        let doneAt =
            fixedEnd
            |> Map.fold (fun (m: Map<string, float>) k e -> m.Add(k, latest m k e)) doneAt
            |> Map.filter (fun k _ -> not (pendingIds.Contains k))

        loop fixedPlan doneAt work []

    /// Most important first (tasks.csv uses the scheduler convention: higher =
    /// more important).
    let byPriority (t: DroneTask) = (-float t.Priority, 0.0, t.Id)

    /// The dispatch the re-plans after a dropout use: most important first.
    let dispatch = dispatchBy byPriority

    /// When a dispatch is done: the last visit's end.
    let makespanOf (plan: Map<string, Visit list>) =
        plan
        |> Map.toList
        |> List.collect snd
        |> List.map (fun v -> v.EndS)
        |> List.fold max 0.0

    /// The mission's dispatch. One greedy pass places the ready tasks in a
    /// given order, and a single order is a heuristic: most important first
    /// can make a long chain of dependent tasks wait behind a short one. So
    /// passes start from three orders (most important first; longest remaining
    /// chain first, a task's own time and the longest chain of tasks waiting
    /// on it; most important first, longest chain next), then the best of them
    /// is improved by swapping neighbouring tasks while a swap helps, within
    /// `maxPasses`. A plan is better when it leaves fewer tasks unflown, then
    /// finishes sooner, then finishes the important tasks earlier.
    let dispatchBest
        (fleet: Aircraft list)
        (work: (DroneTask * Visit) list)
        (fixedPlan: Map<string, Visit list>)
        (doneAt: Map<string, float>)
        (notBefore: float)
        =
        let maxPasses = 40
        let tasks = work |> List.map fst |> List.distinctBy (fun t -> t.Id)
        let byId = tasks |> List.map (fun t -> (t.Id, t)) |> Map.ofList

        let rec chainMin (t: DroneTask) : float =
            let waiting = tasks |> List.filter (fun x -> x.DependsOn |> List.contains t.Id)

            Scheduler.effectiveDurationMin t
            + (waiting |> List.map chainMin |> List.fold max 0.0)

        let chain = tasks |> List.map (fun t -> (t.Id, chainMin t)) |> Map.ofList

        // Whether `a` must finish before `b` can start, directly or through
        // other tasks: swapping such a pair changes nothing.
        let rec before (a: string) (b: string) =
            match byId.TryFind b with
            | Some t -> t.DependsOn |> List.exists (fun d -> d = a || before a d)
            | None -> false

        let priority =
            tasks |> List.map (fun t -> (t.Id, float t.Priority + 1.0)) |> Map.ofList

        let score (plan: Map<string, Visit list>, unassigned: (string * string) list) =
            let weightedFinish =
                plan
                |> Map.toList
                |> List.collect snd
                |> List.sumBy (fun v -> (priority.TryFind(taskOf v.TaskId) |> Option.defaultValue 1.0) * v.EndS)

            (unassigned.Length, makespanOf plan, weightedFinish)

        let tried =
            Collections.Generic.Dictionary<string, (Map<string, Visit list> * (string * string) list)>()

        let run (sequence: string list) =
            let k = String.Join(",", sequence)

            match tried.TryGetValue k with
            | true, r -> Some r
            | _ when tried.Count >= maxPasses -> None
            | _ ->
                let rank = sequence |> List.mapi (fun i id -> (id, float i)) |> Map.ofList

                let r =
                    dispatchBy (fun t -> (rank.[t.Id], 0.0, t.Id)) fleet work fixedPlan doneAt notBefore

                tried.[k] <- r
                Some r

        let orderBy key =
            tasks |> List.sortBy key |> List.map (fun t -> t.Id)

        let starts =
            [
                orderBy byPriority
                orderBy (fun t -> (-chain.[t.Id], -float t.Priority, t.Id))
                orderBy (fun t -> (-float t.Priority, -chain.[t.Id], t.Id))
            ]
            |> List.distinct
            |> List.choose (fun s -> run s |> Option.map (fun r -> (s, r)))

        let rec climb (sequence: string list) (best: Map<string, Visit list> * (string * string) list) =
            let items = Array.ofList sequence

            let swapped =
                seq {
                    for i in 0 .. items.Length - 2 do
                        if not (before items.[i] items.[i + 1]) then
                            let s = Array.copy items
                            s.[i] <- items.[i + 1]
                            s.[i + 1] <- items.[i]
                            yield List.ofArray s
                }

            let better =
                swapped
                |> Seq.map (fun s -> (s, run s))
                |> Seq.takeWhile (fun (_, r) -> r.IsSome)
                |> Seq.tryPick (fun (s, r) ->
                    match r with
                    | Some r when score r < score best -> Some(s, r)
                    | _ -> None)

            match better with
            | Some(s, r) -> climb s r
            | None -> best

        match starts with
        | [] -> dispatch fleet work fixedPlan doneAt notBefore
        | _ ->
            let s, r = starts |> List.minBy (fun (_, r) -> score r)
            climb s r

    /// A dispatch as the library's Solution type, so the schedule output,
    /// Gantt chart and metrics stay as they are; each assignment names its
    /// aircraft as the resource.
    let toSolution (plan: Map<string, Visit list>) (unassigned: (string * string) list) : Solution =
        let assignments =
            plan
            |> Map.toList
            |> List.collect (fun (id, vs) ->
                vs
                |> List.map (fun v ->
                    {
                        TaskId = v.TaskId
                        StartTime = TimeSpan.FromSeconds v.StartS
                        EndTime = TimeSpan.FromSeconds v.EndS
                        AssignedResources = Map.ofList [ (id, 1.0) ]
                    }))
            |> List.sortBy (fun a -> a.StartTime)

        let makespan =
            assignments |> List.map (fun a -> a.EndTime) |> List.fold max TimeSpan.Zero

        {
            Assignments = assignments
            Makespan = makespan
            TotalCost = 0.0
            ResourceUtilization =
                plan
                |> Map.map (fun _ vs ->
                    if makespan.TotalSeconds > 0.0 then
                        (vs |> List.sumBy (fun v -> v.EndS - v.StartS)) / makespan.TotalSeconds
                    else
                        0.0)
            DeadlineViolations = []
            IsValid = unassigned.IsEmpty
        }

    /// Tasks placed on the map with their durations (start 0), ready to dispatch.
    let work (origin: Waypoint) (place: Map<string, Waypoint>) (tasks: DroneTask list) =
        tasks
        |> List.choose (fun t ->
            place.TryFind t.WaypointId
            |> Option.map (fun w ->
                (t,
                 {
                     TaskId = t.Id
                     WaypointId = w.Id
                     Pos = project origin w
                     StartS = 0.0
                     EndS = Scheduler.effectiveDurationMin t * 60.0
                     PayloadKg = t.PayloadKg
                     DependsOn = t.DependsOn
                 })))

    /// Every aircraft's own RTL layer, so no two fallbacks share a level:
    /// copters from this high for the first copter in drones.csv, one step
    /// higher for each next; fixed-wings from the ceiling down. A QuadPlane
    /// flies its RTL as a fixed-wing, so it crosses the base at its layer while
    /// copters climb vertically over their pads: at the ceiling it stays above
    /// every copter climb.
    [<Literal>]
    let rtlBaseM = 60.0

    [<Literal>]
    let rtlStepM = 10.0

    let rtlAltitude (fleet: Aircraft list) (a: Aircraft) =
        let k =
            fleet
            |> List.filter (fun x -> x.FixedWing = a.FixedWing)
            |> List.findIndex (fun x -> x.Drone.Id = a.Drone.Id)

        if a.FixedWing then
            // Whole metres: ArduPlane stores Q_RTL_ALT as an integer.
            Math.Floor Regulations.maxAltitudeAglMeters - float k * rtlStepM
        else
            rtlBaseM + float k * rtlStepM

    let private separationMethod =
        sprintf
            "every flight's 3-D track (straight legs as flown, a hover or a fixed-wing's circle over each task) sampled every %.0f s; pairs both below %.1f m (on the ground on their pads) ignored"
            sampleStepS
            groundZ

    /// Where on its circle a circling fixed-wing is cannot be planned (it joins
    /// the circle wherever it arrives), so while it circles, the closest another
    /// aircraft comes to it is the closest that aircraft comes to the circle:
    /// the horizontal gap to the circle's rim and the height difference.
    let private loiterClearance (o: Outcome) =
        let all = circles o.Sorties

        o.Tracks
        |> List.choose (fun track ->
            circleClearanceAt all track
            |> Option.map (fun (d, t, who) ->
                let _, _, radius, _, _ = all |> List.find (fun (w, _, _, _, _) -> w = who)
                (d, t, who, track.AircraftId, radius)))
        |> List.sortBy (fun (d, _, _, _, _) -> d)
        |> List.tryHead

    let private separation (o: Outcome) =
        let check =
            Ev.closestApproach sampleStepS groundZ o.Tracks
            |> Ev.Checks.separation Safety.minSwarmSeparationMeters separationMethod

        let check =
            match loiterClearance o with
            | Some(d, t, circling, other, radius) ->
                let detail =
                    sprintf
                        "%s circling (radius %.0f m, anywhere on its circle) and %s: %.1f m at t=%.0f s"
                        circling
                        radius
                        other
                        d
                        t

                if d < Safety.minSwarmSeparationMeters then
                    { check with
                        Status = Ev.Fail
                        Measured = check.Measured + $"; %.1f{d} m to a circling fixed-wing's circle"
                        Details = detail :: check.Details
                    }
                else
                    { check with
                        Details = check.Details @ [ detail ]
                    }
            | None -> check

        let pairs = closePairs o.Tracks

        if pairs.Length > 1 then
            { check with
                Details =
                    check.Details
                    @ (pairs
                       |> List.truncate 10
                       |> List.map (fun c ->
                           sprintf
                               "%s / %s: %.1f m at t=%.0f s, (%.0f, %.0f, %.0f) m"
                               c.A
                               c.B
                               c.Distance
                               c.TimeS
                               c.Where.X
                               c.Where.Y
                               c.Where.Z))
            }
        else
            check

    /// Longest dependency chain, for explaining an infeasible schedule.
    let private longestChain (tasks: DroneTask list) =
        let byId = tasks |> List.map (fun t -> (t.Id, t)) |> Map.ofList

        let rec chain (seen: Set<string>) (id: string) : string list =
            match byId.TryFind id with
            | Some t when not (seen.Contains id) ->
                match t.DependsOn |> List.map (chain (seen.Add id)) with
                | [] -> [ id ]
                | xs -> (xs |> List.maxBy List.length) @ [ id ]
            | _ -> []

        match tasks |> List.map (fun t -> chain Set.empty t.Id) with
        | [] -> []
        | xs -> xs |> List.maxBy List.length

    [<Literal>]
    let private planClaim =
        "The plan gives every task a start time, a place and an aircraft"

    /// A pack for when there is no flyable plan to measure: the missing plan is
    /// the failure, and nothing else can be evidenced.
    let blocked (operation: string) (measured: string) (details: string list) (pilots: int) (fleetSize: int) : Ev.Pack =
        let why = "No plan to measure: " + measured

        {
            Example = "SwarmTaskAllocation"
            Operation = operation
            Pilots = pilots
            Aircraft = fleetSize
            PeakAirborne = 0
            Checks =
                [
                    ({
                        Area = Ev.Deconfliction
                        Claim = planClaim
                        Method = "scheduler result joined with waypoints.csv and drones.csv"
                        Measured = measured
                        Limit = "every task scheduled, placed and assigned"
                        Status = Ev.Fail
                        Details = details
                    }
                    : Ev.Check)
                    Ev.Checks.notEvidenced
                        Ev.Deconfliction
                        "No two aircraft are ever closer than the minimum separation"
                        why
                    Ev.Checks.notEvidenced
                        Ev.EnduranceRange
                        "Every aircraft completes its flying with the battery reserve intact"
                        why
                    Ev.Checks.notEvidenced Ev.C2Link "Every planned point is within C2 link range" why
                    Ev.Checks.notEvidenced
                        Ev.AltitudeCeiling
                        "Every planned point is at or below the altitude ceiling"
                        why
                    Ev.Checks.notEvidenced
                        Ev.Contingency
                        "Losing one aircraft leaves the operation within the other checks"
                        why
                    Ev.Checks.notEvidenced Ev.PilotRatio "Pilot-to-aircraft ratio requested" why
                    Ev.Checks.notEvidenced
                        Ev.SupervisorWorkload
                        "No contingency asks more of the pilots at once than there are pilots"
                        why
                ]
            Assumptions = []
        }

    /// The pack when the scheduler returned nothing.
    let noSchedule (tasks: DroneTask list) (reason: string) (pilots: int) (fleetSize: int) : Ev.Pack =
        let chain = longestChain tasks

        blocked
            "Drone swarm task allocation (the scheduler produced no schedule)"
            "no schedule"
            [
                "scheduler: " + reason
                sprintf
                    "%d tasks; longest dependency chain %d tasks in sequence (%s)"
                    tasks.Length
                    chain.Length
                    (String.Join(" -> ", chain))
            ]
            pilots
            fleetSize

    let build
        (tasks: DroneTask list)
        (drones: DroneResource list)
        (waypoints: Waypoint list)
        (baseId: string)
        (pilots: int)
        (methodUsed: string)
        (c2BandMhz: float)
        (dispatched: Map<string, Visit list> option)
        (solution: Solution)
        : Ev.Pack * Outcome option =
        match waypoints |> List.tryFind (fun w -> w.Id = baseId) with
        | None ->
            (blocked
                "Drone swarm task allocation"
                $"base waypoint %s{baseId} not found"
                [
                    $"%d{waypoints.Length} waypoints loaded; pass --base <waypoint id> and --waypoints <path>"
                ]
                pilots
                drones.Length,
             None)
        | Some origin ->
            let place = waypoints |> List.map (fun w -> (w.Id, w)) |> Map.ofList
            let taskById = tasks |> List.map (fun t -> (t.Id, t)) |> Map.ofList
            let droneIds = drones |> List.map (fun d -> d.Id) |> Set.ofList

            // --- The plan as flights ---------------------------------------------
            let placed =
                solution.Assignments
                |> List.map (fun a ->
                    match taskById.TryFind a.TaskId with
                    | None -> Error $"%s{a.TaskId}: not in tasks.csv"
                    | Some t ->
                        match place.TryFind t.WaypointId with
                        | None -> Error $"%s{t.Id}: waypoint %s{t.WaypointId} not in waypoints.csv"
                        | Some w ->
                            Ok
                                {
                                    TaskId = t.Id
                                    WaypointId = w.Id
                                    Pos = project origin w
                                    StartS = a.StartTime.TotalSeconds
                                    EndS = a.EndTime.TotalSeconds
                                    PayloadKg = t.PayloadKg
                                    DependsOn = t.DependsOn
                                })

            // The classical dispatcher hands over its plan as flown (relief
            // stations included); a library schedule is placed from its times.
            let visits, unplaced =
                match dispatched with
                | Some plan -> (plan |> Map.toList |> List.collect snd, [])
                | None ->
                    (placed
                     |> List.choose (function
                         | Ok v -> Some v
                         | Error _ -> None),
                     placed
                     |> List.choose (function
                         | Error e -> Some e
                         | Ok _ -> None))

            let unscheduled =
                tasks
                |> List.filter (fun t -> visits |> List.forall (fun v -> taskOf v.TaskId <> t.Id))
                |> List.map (fun t -> $"%s{t.Id}: not in the schedule")

            // Aircraft the schedule itself names (a resource key that is a drone id).
            let named =
                match dispatched with
                | Some plan ->
                    plan
                    |> Map.toList
                    |> List.collect (fun (id, vs) -> vs |> List.map (fun v -> (v.TaskId, id)))
                    |> Map.ofList
                | None ->
                    solution.Assignments
                    |> List.choose (fun a ->
                        match
                            a.AssignedResources
                            |> Map.toList
                            |> List.map fst
                            |> List.filter droneIds.Contains
                        with
                        | [ id ] -> Some(a.TaskId, id)
                        | _ -> None)
                    |> Map.ofList

            let resourceKeys =
                solution.Assignments
                |> List.collect (fun a -> a.AssignedResources |> Map.toList |> List.map fst)
                |> List.distinct

            let fleet = fleetOf drones

            let noSpeed =
                drones
                |> List.filter (fun d -> fleet |> List.forall (fun a -> a.Drone.Id <> d.Id))
                |> List.map (fun d ->
                    $"%s{d.Id}: no usable cruise_speed_ms / max_range_km / battery_capacity_wh; not flown")

            let flown = fly fleet named visits

            // --- Plan completeness -------------------------------------------------
            let plan =
                let problems = unscheduled @ unplaced @ (flown.Unassigned |> List.map snd) @ noSpeed
                let scheduledIds = visits |> List.map (fun v -> taskOf v.TaskId) |> List.distinct

                // Two tasks.csv rows sharing an id would both count as scheduled.
                let duplicateIds =
                    tasks
                    |> List.countBy (fun t -> t.Id)
                    |> List.filter (fun (_, n) -> n > 1)
                    |> List.map (fun (id, n) ->
                        $"%s{id}: %d{n} tasks share this id, one schedule entry cannot cover them")

                {
                    Area = Ev.Deconfliction
                    Claim = planClaim
                    Method = "scheduler assignments joined with waypoints.csv (place) and drones.csv (aircraft)"
                    Measured =
                        sprintf
                            "%d of %d tasks scheduled, %d visits placed (relief shifts count separately), %d name an aircraft"
                            scheduledIds.Length
                            tasks.Length
                            visits.Length
                            named.Count
                    Limit = "all tasks scheduled, placed and assigned by the plan"
                    Status =
                        if
                            problems.IsEmpty
                            && duplicateIds.IsEmpty
                            && scheduledIds.Length = tasks.Length
                            && named.Count = visits.Length
                        then
                            Ev.Pass
                        else
                            Ev.Fail
                    Details =
                        [
                            if named.Count < visits.Length then
                                sprintf
                                    "the schedule's resource keys are [%s], not drone ids: it says when each task runs but not which aircraft flies it, and the drones' payload capacities are never checked against those keys"
                                    (String.Join(", ", resourceKeys))

                                "the checks below use the first-fit dispatch described in Assumptions; the permission would rest on that dispatch, not on the scheduler's output"
                            yield! duplicateIds @ problems
                        ]
                }
                : Ev.Check

            // --- Deconfliction -----------------------------------------------------
            let sepCheck = separation flown

            // --- Endurance and range -----------------------------------------------
            let lates = lateArrivals flown
            let heavy = overweight flown

            let flyable =
                {
                    Area = Ev.EnduranceRange
                    Claim =
                        "The schedule can be flown: each aircraft reaches every task by its start at cruise speed, within its payload limit"
                    Method =
                        "straight legs at cruise_speed_ms from the previous task (or the base) vs. the scheduled start; payload of a sortie's tasks vs. max_payload_kg"
                    Measured =
                        sprintf
                            "%d of %d tasks reached late%s; %d of %d sortie(s) over payload"
                            lates.Length
                            visits.Length
                            (match lates |> List.sortByDescending (fun (_, _, dt) -> dt) with
                             | (s, t, dt) :: _ -> sprintf " (worst %s by %s, %.0f s)" t (label s) dt
                             | [] -> "")
                            heavy.Length
                            flown.Sorties.Length
                    Limit = "arrival <= scheduled start; payload <= max_payload_kg"
                    Status = if lates.IsEmpty && heavy.IsEmpty then Ev.Pass else Ev.Fail
                    Details =
                        (lates
                         |> List.sortByDescending (fun (_, _, dt) -> dt)
                         |> List.map (fun (s, t, dt) -> sprintf "%s reaches %s %.0f s after its start" (label s) t dt))
                        @ (heavy
                           |> List.map (fun s ->
                               sprintf
                                   "%s carries %.1f kg, max %.1f kg"
                                   (describe s)
                                   s.PayloadKg
                                   s.Aircraft.Drone.MaxPayloadKg))
                }
                : Ev.Check

            let rangeCheck =
                let c =
                    flown.Sorties
                    |> List.map rangeLeg
                    |> Ev.Checks.endurance
                        "km"
                        "per sortie, 3-D path length (base -> tasks -> base) vs. max_range_km less the reserve"

                { c with
                    Claim = c.Claim + ": distance vs. rated range"
                }

            let timeCheck =
                let c =
                    flown.Sorties
                    |> List.map timeLeg
                    |> Ev.Checks.endurance
                        "min"
                        "per sortie, airborne time vs. DroneDomain.estimateRemainingFlightTime at the sortie's mean power; power per segment from DroneDomain.estimatePowerConsumption (hover, forward-flight and climb factors) with an equivalent mass that flies the rated range at cruise speed, plus the sortie's payload"

                { c with
                    Claim = c.Claim + ": battery energy incl. hover"
                }

            // --- C2 and altitude ---------------------------------------------------
            let used =
                visits
                |> List.map (fun v -> v.WaypointId)
                |> List.distinct
                |> List.map (fun id -> place.[id])

            let pointName (w: Waypoint) = $"%s{w.Id} %s{w.Name}"

            let c2 =
                used
                |> List.map (fun w ->
                    let p = project origin w
                    (pointName w, Math.Sqrt(p.X * p.X + p.Y * p.Y) / 1000.0))
                |> Ev.Checks.c2Link (sprintf "the pilot station at %s" (pointName origin)) c2BandMhz c2FadeMarginDb

            // --- Contingency: each aircraft in turn drops out ------------------------
            let tally (o: Outcome) =
                sprintf
                    "%d unassignable, %d late, %d over payload, %d over range, %d over battery; closest approach %s"
                    o.Unassigned.Length
                    (lateArrivals o).Length
                    (overweight o).Length
                    (o.Sorties |> List.map rangeLeg |> short).Length
                    (o.Sorties |> List.map timeLeg |> short).Length
                    (Ev.closestApproach sampleStepS groundZ o.Tracks
                     |> Option.map (fun c -> $"%.1f{c.Distance} m")
                     |> Option.defaultValue "-")

            let allWork = work origin place tasks

            let flightsOf (plan: Map<string, Visit list>) =
                let named =
                    plan
                    |> Map.toList
                    |> List.collect (fun (id, vs) -> vs |> List.map (fun v -> (v.TaskId, id)))
                    |> Map.ofList

                (named, plan |> Map.toList |> List.collect snd)

            // The whole mission re-dispatched without each aircraft in turn, as
            // if it failed before launch: once, for both the contingency checks
            // and the workload.
            let preLaunchReplans =
                fleet
                |> List.map (fun lost ->
                    let rest = fleet |> List.filter (fun a -> a.Drone.Id <> lost.Drone.Id)
                    (lost.Drone.Id, (rest, dispatchBest rest allWork Map.empty Map.empty 0.0)))
                |> Map.ofList

            let dropOut (lost: Aircraft) =
                let rest, (replanned, left) = preLaunchReplans.[lost.Drone.Id]
                let n, vs = flightsOf replanned
                let o = { fly rest n vs with Unassigned = left }

                let moved =
                    flown.Assignment
                    |> Map.toList
                    |> List.filter (fun (_, id) -> id = lost.Drone.Id)
                    |> List.map (fun (t, _) ->
                        sprintf "%s -> %s" t (o.Assignment.TryFind t |> Option.defaultValue "re-planned or nobody"))

                let lates = lateArrivals o
                let heavy = overweight o
                let overRange = o.Sorties |> List.map rangeLeg |> short
                let overTime = o.Sorties |> List.map timeLeg |> short
                let closest = Ev.closestApproach sampleStepS groundZ o.Tracks

                let tooClose =
                    closest |> Option.exists (fun c -> c.Distance < Safety.minSwarmSeparationMeters)

                // Absorbing a loss means the rest stay safe and within limits; a
                // task the remaining fleet physically cannot fly is dropped by the
                // pilot, named here, and counted as a decision in the workload.
                let ok =
                    lates.IsEmpty
                    && heavy.IsEmpty
                    && overRange.IsEmpty
                    && overTime.IsEmpty
                    && not tooClose

                Ev.Checks.contingency
                    (sprintf
                        "Without %s (%s) the rest of the fleet flies a safe re-plan, and any task it cannot fly is named"
                        lost.Drone.Id
                        lost.Drone.Model)
                    "the classical dispatcher re-plans the whole mission with the remaining aircraft (new times where it must), and every check above is re-run on that plan"
                    (sprintf
                        "without %s: %d task(s) moved; %s%s"
                        lost.Drone.Id
                        moved.Length
                        (tally o)
                        (match o.Unassigned with
                         | [] -> ""
                         | dropped -> sprintf "; the pilot drops %s" (dropped |> List.map fst |> String.concat ", ")))
                    (if ok then Ev.Pass else Ev.Fail)
                    // The whole-fleet figures first, so a failure the loss causes
                    // can be told from one the plan already had.
                    ((sprintf "whole fleet, for comparison: %s" (tally flown)) :: moved
                     @ (o.Unassigned |> List.map snd)
                     @ (lates
                        |> List.map (fun (s, t, dt) -> sprintf "%s reaches %s %.0f s late" (label s) t dt))
                     @ (overRange
                        |> List.map (fun (w, need, have) -> $"%s{w} needs %.1f{need} km, has %.1f{have}"))
                     @ (overTime
                        |> List.map (fun (w, need, have) -> $"%s{w} needs %.1f{need} min, has %.1f{have}"))
                     @ (if tooClose then
                            closest
                            |> Option.map (fun c -> [ $"%s{c.A} / %s{c.B} %.1f{c.Distance} m at t=%.0f{c.TimeS} s" ])
                            |> Option.defaultValue []
                        else
                            []))

            // --- In-flight dropout: the swarm absorbs it ------------------------------
            // Losing an aircraft mid-flight (lost link, low battery, a fault) is
            // expected. It holds when its fallback never comes near anyone still
            // flying (else they would have to react: a cascade), and the tasks it
            // leaves are re-dispatched to the others without a person deciding.
            let dropoutStepS = 10.0

            // RTL: to the aircraft's layer, home at that height, down onto the pad.
            // A copter's RTL climbs to the layer but never descends to it; a
            // QuadPlane's goes to the layer either way.
            // A copter climbs in place first; a QuadPlane flies its RTL as a
            // fixed-wing (Q_RTL_MODE 1), changing height on the way home.
            let fallbackTrack (id: string) (a: Aircraft) (rtlAltM: float) (t: float) (p: Ev.P3) =
                let layer = if a.FixedWing then rtlAltM else max p.Z rtlAltM
                let over = { a.Pad with Z = layer }

                let toOver =
                    if a.FixedWing then
                        [| (t + travelS a p over + Mav.Flight.vtolApproachS, over) |]
                    else
                        let up = { p with Z = layer }
                        let t1 = t + travelS a p up
                        [| (t1, up); (t1 + travelS a up over, over) |]

                let t2 = fst (Array.last toOver)

                ({
                    AircraftId = id
                    Samples =
                        Array.concat
                            [
                                [| (t, p) |]
                                toOver
                                [| (t2 + Mav.Flight.landingS (vehicleOf a) layer, a.Pad) |]
                            ]
                }
                : Ev.Track)

            let trackLengthKm (samples: (float * Ev.P3)[]) =
                samples
                |> Array.pairwise
                |> Array.sumBy (fun ((_, a), (_, b)) -> Ev.dist3 a b)
                |> fun m -> m / 1000.0

            let planOf (o: Outcome) =
                o.Sorties
                |> List.groupBy (fun s -> s.Aircraft.Drone.Id)
                |> List.map (fun (id, ss) -> (id, ss |> List.collect (fun s -> s.Visits)))
                |> Map.ofList

            let flownPlan = planOf flown

            let dropouts =
                flown.Sorties
                |> List.map (fun s ->
                    let a = s.Aircraft

                    let mine =
                        ({
                            AircraftId = label s
                            Samples = s.Samples
                        }
                        : Ev.Track)

                    let others =
                        flown.Tracks |> List.filter (fun tr -> tr.AircraftId <> mine.AircraftId)

                    let launch = fst s.Samples.[0]
                    let landing = fst (Array.last s.Samples)
                    // One layer per aircraft: its own sorties never overlap in time.
                    let rtlAlt = rtlAltitude fleet a

                    [ launch + dropoutStepS .. dropoutStepS .. landing - 1.0 ]
                    |> List.choose (fun t ->
                        Ev.positionAt mine t
                        |> Option.map (fun p ->
                            let flownKm =
                                trackLengthKm (
                                    Array.append (s.Samples |> Array.filter (fun (ti, _) -> ti <= t)) [| (t, p) |]
                                )

                            let home = fallbackTrack (label s + " fallback") a rtlAlt t p

                            let usableKm = a.Drone.MaxRangeKm * usableFraction
                            let goesHome = flownKm + trackLengthKm home.Samples <= usableKm

                            // Out of range to get home, the aircraft still flies
                            // the RTL its failsafes command and lands where the
                            // critical-battery failsafe finds it. Where that is
                            // depends on the battery, so both ends are checked.
                            let fallbacks =
                                if goesHome then
                                    [ home ]
                                else
                                    [
                                        home
                                        home
                                        |> Ev.flownThenLanding
                                            (label s + " landing")
                                            Safety.imuFailureDescentRateMs
                                            ((usableKm - flownKm) * 1000.0)
                                    ]

                            let closest =
                                [
                                    for fb in fallbacks do
                                        for o in others do
                                            match Ev.closestApproach sampleStepS groundZ [ fb; o ] with
                                            | Some c -> c
                                            | None -> ()
                                ]
                                |> List.sortBy (fun c -> c.Distance)
                                |> List.tryHead

                            // Everything the aircraft had not finished goes back to
                            // the dispatcher, one piece per task (the remaining time
                            // of all its unfinished shifts of that task).
                            let ownVisits = flownPlan.TryFind a.Drone.Id |> Option.defaultValue []
                            let leftovers = ownVisits |> List.filter (fun v -> v.EndS > t)

                            let leftWork =
                                leftovers
                                |> List.groupBy (fun v -> taskOf v.TaskId)
                                |> List.choose (fun (id, vs) ->
                                    taskById.TryFind id
                                    |> Option.map (fun task ->
                                        (task,
                                         { List.head vs with
                                             TaskId = id
                                             StartS = 0.0
                                             EndS = vs |> List.sumBy (fun v -> v.EndS - max v.StartS t)
                                         })))

                            let doneAt =
                                ownVisits
                                |> List.filter (fun v -> v.EndS <= t)
                                |> List.groupBy (fun v -> taskOf v.TaskId)
                                |> List.map (fun (id, vs) -> (id, vs |> List.map (fun v -> v.EndS) |> List.max))
                                |> Map.ofList

                            let absorbedBy =
                                if leftWork.IsEmpty then
                                    Some []
                                else
                                    let rest = fleet |> List.filter (fun x -> x.Drone.Id <> a.Drone.Id)
                                    let fixedPlan = flownPlan.Remove a.Drone.Id
                                    let replanned, left = dispatch rest leftWork fixedPlan doneAt t

                                    if left.IsEmpty then
                                        // The aircraft that took on a piece.
                                        Some(
                                            replanned
                                            |> Map.toList
                                            |> List.filter (fun (id, vs) ->
                                                vs.Length >
                                                    (fixedPlan.TryFind id
                                                     |> Option.map List.length
                                                     |> Option.defaultValue 0))
                                            |> List.map fst
                                        )
                                    else
                                        None

                            {|
                                Aircraft = label s
                                AircraftId = a.Drone.Id
                                TimeS = t
                                GoesHome = goesHome
                                FallbackEndS =
                                    fallbacks |> List.map (fun fb -> fst (Array.last fb.Samples)) |> List.max
                                FallbackTopM =
                                    fallbacks
                                    |> List.collect (fun fb ->
                                        fb.Samples |> Array.map (fun (_, q) -> q.Z) |> List.ofArray)
                                    |> List.max
                                Closest = closest
                                Leftovers = leftWork.Length
                                AbsorbedBy = absorbedBy
                            |})))

            // Every point a fallback reaches is planned too: an RTL layer above
            // the ceiling (too many aircraft to stack) fails here, openly.
            let altitude =
                let fallbacks =
                    dropouts
                    |> List.concat
                    |> List.groupBy (fun x -> x.AircraftId)
                    |> List.map (fun (id, xs) ->
                        ($"%s{id} fallback (RTL layer)", xs |> List.map (fun x -> x.FallbackTopM) |> List.max))

                (used |> List.map (fun w -> (pointName w, w.AltitudeM))) @ fallbacks
                |> Ev.Checks.altitude

            let dropoutCheck =
                let all = List.concat dropouts

                let conflicts =
                    all
                    |> List.filter (fun x ->
                        x.Closest
                        |> Option.exists (fun c -> c.Distance < Safety.minSwarmSeparationMeters))

                let unabsorbed = all |> List.filter (fun x -> x.AbsorbedBy.IsNone)

                let worst =
                    all
                    |> List.choose (fun x -> x.Closest |> Option.map (fun c -> (x, c)))
                    |> List.sortBy (fun (_, c) -> c.Distance)
                    |> List.tryHead

                Ev.Checks.contingency
                    "Any aircraft can drop out at any moment and the swarm absorbs it: no knock-on conflict, its unfinished tasks re-dispatched where the rest can fly them and named for the pilot where they cannot"
                    (sprintf
                        "every %.0f s along every sortie: the aircraft leaves the plan and flies its fallback (a copter up to its own RTL layer, %.0f m + %.0f m per copter in drones.csv order, or on at its height if higher; a fixed-wing to its layer on the way, the ceiling less %.0f m per fixed-wing before it; straight home, down; or, when its range cannot get it home, the same RTL until the range runs out and a landing there, both checked) while the others fly on; closest approach of the fallback to every other flight, and the classical dispatcher re-plans its unfinished tasks onto the others from that moment"
                        dropoutStepS
                        rtlBaseM
                        rtlStepM
                        rtlStepM)
                    (sprintf
                        "%d dropout moments; closest fallback approach %s; %d knock-on conflict(s); %d leave tasks only the pilot can drop"
                        all.Length
                        (match worst with
                         | Some(x, c) ->
                             $"%.1f{c.Distance} m (%s{x.Aircraft} dropping at t=%.0f{x.TimeS} s, vs %s{c.B})"
                         | None -> "n/a (no other aircraft airborne)")
                        conflicts.Length
                        unabsorbed.Length)
                    // Safety decides; tasks nobody can take become pilot decisions (workload).
                    (if conflicts.IsEmpty then Ev.Pass else Ev.Fail)
                    ((conflicts
                      |> List.truncate 5
                      |> List.map (fun x ->
                          sprintf
                              "conflict: %s dropping at t=%.0f s passes %.1f m from %s"
                              x.Aircraft
                              x.TimeS
                              x.Closest.Value.Distance
                              x.Closest.Value.B))
                     @ (unabsorbed
                        |> List.truncate 5
                        |> List.map (fun x ->
                            sprintf
                                "%s dropping at t=%.0f s leaves %d task(s) nobody can take"
                                x.Aircraft
                                x.TimeS
                                x.Leftovers)))

            // --- Supervisor workload --------------------------------------------------
            let workload =
                let event what affected start duration handling response =
                    {
                        Ev.Event = what
                        Ev.Affected = affected
                        Ev.StartS = start
                        Ev.DurationS = duration
                        Ev.Handling = handling
                        Ev.Response = response
                    }

                let inFlight =
                    dropouts
                    |> List.concat
                    |> List.map (fun x ->
                        let name = $"%s{x.Aircraft} drops out at t=%.0f{x.TimeS} s"

                        (name,
                         [
                             event
                                 name
                                 1
                                 x.TimeS
                                 (x.FallbackEndS - x.TimeS)
                                 Ev.Automatic
                                 (if x.GoesHome then
                                      "fallback: own RTL layer, straight home"
                                  else
                                      "fallback: RTL toward its own pad, landing where the range runs out")
                             match x.Closest with
                             | Some c when c.Distance < Safety.minSwarmSeparationMeters ->
                                 event
                                     $"passes %.1f{c.Distance} m from %s{c.B}"
                                     1
                                     x.TimeS
                                     Ev.decisionTimeS
                                     Ev.PilotDecision
                                     $"divert %s{c.B}"
                             | _ -> ()
                             if x.Leftovers > 0 then
                                 match x.AbsorbedBy with
                                 | Some by ->
                                     event
                                         $"%d{x.Leftovers} task(s) left"
                                         (List.length by)
                                         x.TimeS
                                         60.0
                                         Ev.Automatic
                                         (sprintf "dispatcher re-plans them onto %s" (String.Join(", ", by)))
                                 | None ->
                                     event
                                         $"%d{x.Leftovers} task(s) left"
                                         0
                                         x.TimeS
                                         Ev.decisionTimeS
                                         Ev.PilotDecision
                                         "decide which tasks the mission drops"
                         ]))

                let preLaunch =
                    fleet
                    |> List.map (fun lost ->
                        let _, (_, left) = preLaunchReplans.[lost.Drone.Id]
                        let name = $"%s{lost.Drone.Id} fails before launch"

                        (name,
                         [
                             event
                                 name
                                 1
                                 0.0
                                 Ev.decisionTimeS
                                 Ev.PilotDecision
                                 (match left with
                                  | [] -> "approve the re-dispatched plan"
                                  | dropped ->
                                      sprintf
                                          "approve the re-plan, dropping %s"
                                          (dropped |> List.map fst |> String.concat ", "))
                         ]))

                Ev.Checks.workload pilots (inFlight @ preLaunch)

            // --- Ratio -------------------------------------------------------------
            let peak = Ev.peakAirborne sampleStepS flown.Tracks

            ({
                Example = "SwarmTaskAllocation"
                Operation =
                    sprintf
                        "Swarm task allocation, %d tasks on %d aircraft from %s, makespan %.0f min (%s)"
                        tasks.Length
                        drones.Length
                        (pointName origin)
                        solution.Makespan.TotalMinutes
                        methodUsed
                Pilots = pilots
                Aircraft = drones.Length
                PeakAirborne = peak
                Checks =
                    [ plan; sepCheck; flyable; rangeCheck; timeCheck; c2; altitude ]
                    @ (fleet |> List.map dropOut)
                    @ [ dropoutCheck; Ev.Checks.pilotRatio pilots drones.Length peak; workload ]
                Assumptions =
                    [
                        "Aircraft: with the classical method, the dispatcher's own plan (it decides when and who, checked against this same flight model); with the quantum method, the library schedule's times and a first-fit dispatch, since its assignments name no aircraft."
                        sprintf
                            "A watch no single aircraft can hold is flown in relief shifts, the next arriving as the previous leaves, on a station %.0f m to the side and as much higher (lower under the ceiling), so an arriving copter stays clear of a fixed-wing still circling."
                            (2.0 * Safety.minSwarmSeparationMeters)
                        sprintf
                            "Waypoints: altitude_m is metres above ground at the waypoint; positions are projected equirectangularly around the base %s. Every aircraft launches from and lands on its own pad on a ring around the base, climbing and descending vertically."
                            (pointName origin)
                        "Legs are straight 3-D lines flown as the exported ArduPilot missions fly them: a copter from rest to rest on every leg, holding 1 s at every waypoint so that it does come to rest (ArduCopter 4.7's S-curve, checked against SITL), a QuadPlane at cruise speed; climbs at 2.5 m/s, descents at 1.5 m/s and the last metres of a landing at 0.5 m/s, and 4 s of spool-up at launch. An aircraft launches so it reaches its first task exactly at the task's scheduled start."
                        "Every task is flown as a hover (fixed-wing: circling it clockwise at its turn radius, at cruise power) at its waypoint for its whole scheduled interval, including takeoff and return tasks whose scheduled time includes ground overheads: this over-states airborne time."
                        sprintf
                            "Between two tasks an aircraft lands at the base (fresh battery, at least %.0f min on the ground) whenever the gap allows the round trip; otherwise it flies straight on and waits at the next waypoint."
                            Scheduling.minGroundTimeMin
                        sprintf
                            "Energy: DroneDomain's power model (%.0f W/kg hover, forward-flight factor %.1f, climb x%.1f, descent x%.1f) with an equivalent mass per aircraft that flies its rated max_range_km on the full battery at cruise speed; all of a sortie's payload is carried for the whole sortie; reserve %.0f%%."
                            Battery.hoverPowerWPerKg
                            Battery.forwardFlightEfficiencyFactor
                            Battery.climbPowerIncreaseFactor
                            Battery.descentPowerReductionFactor
                            Battery.reserveBatteryPercent
                        sprintf
                            "Pilot station at the base; C2 over %.0f MHz. Legs are straight, so the farthest point of each is a waypoint."
                            c2BandMhz
                        "Times are the scheduler's own. Its quantum sampler is stochastic: a re-run can produce a different schedule and so a different pack."
                        "No traffic other than this fleet."
                    ]
             },
             Some flown)

// =============================================================================
// MAVLINK EXPORT
// =============================================================================

/// ArduPilot 4.7 missions that fly the evidence's tracks: every sortie from
/// its aircraft's own pad, started at its launch time by the generated
/// launcher (mavlink_show.fsx). Copters hover over a task; QuadPlanes circle it.
module Export =

    module Ev = FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence

    /// Local metres around the base back to latitude and longitude (the inverse
    /// of Evidence.project); `z` is metres above the base.
    let private toGeo (origin: Waypoint) (p: Ev.P3) : Mav.GeoCoordinate =
        let r =
            FSharp.Azure.Quantum.Examples.Drones.Domain.Environment.earthRadiusKm * 1000.0

        let rad (deg: float) = deg * Math.PI / 180.0

        {
            Latitude = origin.Latitude + p.Y / r * 180.0 / Math.PI
            Longitude = origin.Longitude + p.X / (r * Math.Cos(rad origin.Latitude)) * 180.0 / Math.PI
            Altitude = p.Z
        }

    let private items (origin: Waypoint) (s: Evidence.Sortie) =
        let a = s.Aircraft
        let vehicle = Evidence.vehicleOf a
        let geo = toGeo origin
        let pad = geo { a.Pad with Z = 0.0 }
        let first = (List.head s.Visits).Pos
        let last = (List.last s.Visits).Pos

        let acceptM =
            match vehicle with
            | Mav.ArduCopter -> 2.0
            | Mav.ArduQuadPlane -> Mav.Flight.turnRadiusM a.SpeedMs

        let items = ResizeArray<Mav.MissionItem>()

        match vehicle with
        | Mav.ArduCopter ->
            items.Add(Mav.takeoff first.Z pad items.Count)
            items.Add(Mav.setSpeed a.SpeedMs items.Count)
        | Mav.ArduQuadPlane -> items.Add(Mav.vtolTakeoff first.Z pad items.Count)

        for v, (arrive, leave) in List.zip s.Visits s.Stays do
            let pos = geo v.Pos

            match vehicle with
            // Arrival counts reaching the waypoint and the settle; the
            // waypoint holds the settle and the rest.
            | Mav.ArduCopter -> Mav.holdAt acceptM pos (leave - arrive + Mav.settleS) items
            | Mav.ArduQuadPlane when leave - arrive >= 0.5 ->
                // Whole seconds on board; rounded down, the orbit never
                // outlasts the endurance the evidence checked.
                items.Add(Mav.orbit (Math.Floor(leave - arrive)) pos items.Count)
            | Mav.ArduQuadPlane -> items.Add(Mav.waypoint 0.0 acceptM pos items.Count)

        match vehicle with
        | Mav.ArduCopter ->
            Mav.stopAt acceptM { pad with Altitude = last.Z } 0.0 items
            items.Add(Mav.landAt pad items.Count)
        | Mav.ArduQuadPlane -> items.Add(Mav.vtolLand pad items.Count)

        (acceptM, List.ofSeq items)

    /// Write the missions, parameter files and launcher to <outDir>/mavlink.
    /// `homeAltM` is the ground at the base above mean sea level. Returns the
    /// number of missions.
    let write
        (outDir: string)
        (origin: Waypoint)
        (fleet: Evidence.Aircraft list)
        (o: Evidence.Outcome)
        (homeAltM: float)
        =
        let index (a: Evidence.Aircraft) =
            fleet |> List.findIndex (fun x -> x.Drone.Id = a.Drone.Id)

        let missions =
            o.Sorties
            |> List.map (fun s ->
                let a = s.Aircraft
                let vehicle = Evidence.vehicleOf a
                let v = Mav.Flight.vertical vehicle
                let k = index a
                let acceptM, items = items origin s

                ({
                    Mission =
                        {
                            Drone =
                                {
                                    SystemId = k + 1
                                    ComponentId = 1
                                    Name = $"%s{a.Drone.Id}_s%d{s.Number}"
                                    ConnectionString = sprintf "tcp:127.0.0.1:%d" (5760 + 10 * k)
                                }
                            Vehicle = vehicle
                            HomePosition =
                                { toGeo origin { a.Pad with Z = 0.0 } with
                                    Altitude = homeAltM
                                }
                            Items = items
                            Parameters =
                                Mav.Params.forProfile
                                    {
                                        Vehicle = vehicle
                                        CruiseMs = a.SpeedMs
                                        ClimbMs = v.ClimbMs
                                        DescentMs = v.DescentMs
                                        RtlAltM = Evidence.rtlAltitude fleet a
                                        WaypointRadiusM = acceptM
                                        // Lost link: the modelled fallback is RTL.
                                        LostLink = Mav.ReturnHome
                                    }
                        }
                    LaunchS = fst s.Samples.[0]
                }
                : Mav.ScheduledMission))

        let dir = Path.Combine(outDir, "mavlink")
        Directory.CreateDirectory dir |> ignore
        let plain = missions |> List.map (fun m -> m.Mission)
        Mav.QGroundControl.writeAll dir plain
        Mav.WaypointFile.writeAll dir plain
        Mav.ParamFile.writeAll dir plain
        Mav.FsxScript.writeFile (Path.Combine(dir, "mavlink_show.fsx")) missions

        // When each mission is planned to start and land, to compare with the
        // launcher's telemetry.csv.
        Reporting.writeCsv
            (Path.Combine(dir, "plan.csv"))
            [ "mission"; "launch_s"; "end_s" ]
            [
                for s in o.Sorties ->
                    [
                        $"%s{s.Aircraft.Drone.Id}_s%d{s.Number}"
                        sprintf "%.1f" (fst s.Samples.[0])
                        sprintf "%.1f" (fst (Array.last s.Samples))
                    ]
            ]

        // Where each mission is planned to be, when: the tracks the evidence checked.
        Reporting.writeCsv
            (Path.Combine(dir, "plan_tracks.csv"))
            [ "mission"; "t_s"; "lat"; "lon"; "rel_alt_m" ]
            [
                for s in o.Sorties do
                    for t, p in s.Samples do
                        let g = toGeo origin p

                        [
                            $"%s{s.Aircraft.Drone.Id}_s%d{s.Number}"
                            $"%.2f{t}"
                            $"%.7f{g.Latitude}"
                            $"%.7f{g.Longitude}"
                            $"%.2f{p.Z}"
                        ]
            ]

        missions.Length

// =============================================================================
// MAIN PROGRAM
// =============================================================================

module Program =

    [<EntryPoint>]
    let main argv =
        let args = Cli.parse argv

        if Cli.hasFlag "help" args || Cli.hasFlag "h" args then
            printfn "╔════════════════════════════════════════════════════════════╗"
            printfn "║  DRONE SWARM TASK ALLOCATION                               ║"
            printfn "║  Quantum-Enhanced Scheduling Optimization                  ║"
            printfn "╠════════════════════════════════════════════════════════════╣"
            printfn "║  Allocates and schedules tasks across a drone fleet        ║"
            printfn "║  respecting dependencies and resource constraints.         ║"
            printfn "╠════════════════════════════════════════════════════════════╣"
            printfn "║  OPTIONS:                                                  ║"
            printfn "║    --tasks <path>    CSV file with task definitions        ║"
            printfn "║    --drones <path>   CSV file with drone specifications    ║"
            printfn "║    --waypoints <p>   CSV file with waypoint positions      ║"
            printfn "║    --base <id>       base / pilot station (default WP001)  ║"
            printfn "║    --pilots <n>      remote pilots, 1:N evidence (def. 1)  ║"
            printfn "║    --out <dir>       Output directory for results          ║"
            printfn "║    --method <m>      classical | quantum (default: class.) ║"
            printfn "║    --c2-band <MHz>   C2 radio band: 900 (default) | 2400   ║"
            printfn "║    --mavlink         Write ArduPilot missions + launcher   ║"
            printfn "║    --home-alt <m>    Base ground above MSL (default 0)     ║"
            printfn "║    --help            Show this help                        ║"
            printfn "╚════════════════════════════════════════════════════════════╝"
            0
        else
            let sw = Stopwatch.StartNew()

            let tasksPath = Cli.getOr "tasks" "examples/Drones/_data/tasks.csv" args
            let dronesPath = Cli.getOr "drones" "examples/Drones/_data/drones.csv" args
            let waypointsPath = Cli.getOr "waypoints" "examples/Drones/_data/waypoints.csv" args
            let baseId = Cli.getOr "base" "WP001" args

            // The C2 radio the operator flies: a long-range 900 MHz link by
            // default (FleetPathPlanning shows the 2.4 GHz + relay-mesh route).
            let c2BandMhz =
                match
                    Double.TryParse(Cli.getOr "c2-band" "900" args, NumberStyles.Float, CultureInfo.InvariantCulture)
                with
                | true, v when v > 0.0 -> v
                | _ -> 900.0

            let pilots = max 1 (Cli.getIntOr "pilots" 1 args)

            let outDir =
                Cli.getOr "out" (Path.Combine("runs", "drone", "swarm-task-allocation")) args

            let method = Cli.getOr "method" "classical" args

            Data.ensureDirectory outDir
            let runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss")

            printfn ""
            printfn "╔════════════════════════════════════════════════════════════╗"
            printfn "║  DRONE SWARM TASK ALLOCATION                               ║"
            printfn "║  FSharp.Azure.Quantum Example                              ║"
            printfn "╚════════════════════════════════════════════════════════════╝"
            printfn ""
            printfn "Loading tasks from: %s" tasksPath
            printfn "Loading drones from: %s" dronesPath
            printfn "Method: %s" method
            printfn ""

            // Read input data
            let tasks, taskErrors = Parse.readTasks tasksPath
            let drones, droneErrors = Parse.readDrones dronesPath
            let waypoints, waypointErrors = Parse.readWaypoints waypointsPath

            if not taskErrors.IsEmpty then
                printfn "⚠ Task parsing errors:"
                taskErrors |> List.iter (printfn "  - %s")

            if not droneErrors.IsEmpty then
                printfn "⚠ Drone parsing errors:"
                droneErrors |> List.iter (printfn "  - %s")

            if not waypointErrors.IsEmpty then
                printfn "⚠ Waypoint parsing errors:"
                waypointErrors |> List.iter (printfn "  - %s")

            if tasks.IsEmpty then
                printfn "❌ No tasks loaded. Exiting."
                1
            else
                printfn
                    "Loaded %d tasks with %d dependencies"
                    tasks.Length
                    (tasks |> List.sumBy (fun t -> t.DependsOn.Length))

                printfn "Loaded %d drones" drones.Length
                printfn ""

                // Build and solve scheduling problem
                let problem = Scheduler.buildProblem tasks drones

                printfn "Solving task allocation problem..."
                printfn ""

                let methodUsed, result =
                    match method.ToLowerInvariant() with
                    | "quantum" ->
                        let backend = LocalBackend() :> IQuantumBackend

                        ("Quantum (LocalBackend)",
                         Scheduler.solveWithQuantum backend problem
                         |> Async.RunSynchronously
                         |> Result.map (fun solution -> (solution, None)))
                    | _ ->
                        // WHEN and WHO together, with the flight between tasks.
                        match waypoints |> List.tryFind (fun w -> w.Id = baseId) with
                        | None ->
                            ("Classical dispatch",
                             Error(
                                 QuantumError.ValidationError(
                                     "base",
                                     $"base waypoint %s{baseId} not in %s{waypointsPath}"
                                 )
                             ))
                        | Some origin ->
                            let place = waypoints |> List.map (fun w -> (w.Id, w)) |> Map.ofList
                            let fleet = Evidence.fleetOf drones
                            let work = Evidence.work origin place tasks
                            let plan, unassigned = Evidence.dispatchBest fleet work Map.empty Map.empty 0.0

                            for _, why in unassigned do
                                printfn "⚠ %s" why

                            ("Classical dispatch (dependency order, task order searched, flight-model checked)",
                             Ok(Evidence.toSolution plan unassigned, Some plan))

                // Helper to get primary resource from AssignedResources map
                let getPrimaryResource (assignedResources: Map<string, float>) : string =
                    assignedResources
                    |> Map.toSeq
                    |> Seq.tryHead
                    |> Option.map fst
                    |> Option.defaultValue "unassigned"

                match result with
                | Error e ->
                    printfn "❌ Scheduling failed: %s" e.Message

                    // No schedule is itself the finding: the permission case has no plan.
                    let evidence = Evidence.noSchedule tasks e.Message pilots drones.Length
                    FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence.print evidence
                    FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence.write outDir evidence
                    1
                | Ok(solution, dispatched) ->
                    sw.Stop()

                    // Print results
                    printfn "╔════════════════════════════════════════════════════════════╗"
                    printfn "║  SCHEDULING RESULTS                                        ║"
                    printfn "╠════════════════════════════════════════════════════════════╣"
                    printfn "║  Method: %-48s ║" methodUsed
                    printfn "║  Makespan: %8.1f minutes                                ║" solution.Makespan.TotalMinutes
                    printfn "║  Tasks Scheduled: %3d                                      ║" solution.Assignments.Length
                    printfn "╠════════════════════════════════════════════════════════════╣"
                    printfn "║  TASK ASSIGNMENTS:                                         ║"

                    solution.Assignments
                    |> List.sortBy (fun a -> a.StartTime)
                    |> List.iter (fun assignment ->
                        let resource = getPrimaryResource assignment.AssignedResources

                        printfn
                            "║  %-12s │ Start: %6.1f │ End: %6.1f │ %s ║"
                            assignment.TaskId
                            assignment.StartTime.TotalMinutes
                            assignment.EndTime.TotalMinutes
                            resource)

                    printfn "╚════════════════════════════════════════════════════════════╝"

                    // Gantt chart
                    let gantt = Visualization.generateGanttChart solution 5.0
                    printfn "%s" gantt

                    // Calculate metrics
                    let totalTaskTime =
                        solution.Assignments
                        |> List.sumBy (fun a -> (a.EndTime - a.StartTime).TotalMinutes)

                    let totalSlotTime = solution.Makespan.TotalMinutes * float (max 1 drones.Length)

                    let utilization =
                        if totalSlotTime > 0.0 then
                            totalTaskTime / totalSlotTime
                        else
                            0.0

                    let idleTime = totalSlotTime - totalTaskTime

                    let tasksSha = Data.fileSha256Hex tasksPath
                    let dronesSha = Data.fileSha256Hex dronesPath

                    let metrics: Metrics =
                        {
                            run_id = runId
                            tasks_path = tasksPath
                            drones_path = dronesPath
                            tasks_sha256 = tasksSha
                            drones_sha256 = dronesSha
                            task_count = tasks.Length
                            drone_count = drones.Length
                            dependency_count = tasks |> List.sumBy (fun t -> t.DependsOn.Length)
                            method_used = methodUsed
                            makespan_min = solution.Makespan.TotalMinutes
                            total_idle_time_min = idleTime
                            resource_utilization = utilization
                            elapsed_ms = sw.ElapsedMilliseconds
                        }

                    Reporting.writeJson (Path.Combine(outDir, "metrics.json")) metrics

                    // Write schedule as CSV
                    let scheduleRows =
                        solution.Assignments
                        |> List.sortBy (fun a -> a.StartTime)
                        |> List.map (fun a ->
                            [
                                a.TaskId
                                $"%.1f{a.StartTime.TotalMinutes}"
                                $"%.1f{a.EndTime.TotalMinutes}"
                                sprintf "%.1f" (a.EndTime - a.StartTime).TotalMinutes
                                getPrimaryResource a.AssignedResources
                            ])

                    Reporting.writeCsv
                        (Path.Combine(outDir, "schedule.csv"))
                        [ "task_id"; "start_time_min"; "end_time_min"; "duration_min"; "resource_id" ]
                        scheduleRows

                    // Write Gantt chart
                    Reporting.writeTextFile (Path.Combine(outDir, "gantt.txt")) gantt

                    // Evidence for flying this schedule under a 1:N permission.
                    let evidence, flown =
                        Evidence.build tasks drones waypoints baseId pilots methodUsed c2BandMhz dispatched solution

                    FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence.print evidence
                    FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence.write outDir evidence

                    // The flights as ArduPilot missions, flying exactly the checked tracks.
                    if Cli.hasFlag "mavlink" args then
                        match flown, waypoints |> List.tryFind (fun w -> w.Id = baseId) with
                        | Some o, Some origin ->
                            let homeAltM = Cli.getFloatOr "home-alt" 0.0 args
                            let n = Export.write outDir origin (Evidence.fleetOf drones) o homeAltM

                            printfn
                                "  %d ArduPilot mission(s) and the launcher written to %s"
                                n
                                (Path.Combine(outDir, "mavlink"))
                        | _ -> printfn "  No flights to export."

                    let evidenceVerdict =
                        FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence.verdict evidence

                    let assignments =
                        solution.Assignments
                        |> List.sortBy (fun a -> a.StartTime)
                        |> List.map (fun a ->
                            sprintf
                                "| %s | %.1f | %.1f | %.1f | %s |"
                                a.TaskId
                                a.StartTime.TotalMinutes
                                a.EndTime.TotalMinutes
                                (a.EndTime - a.StartTime).TotalMinutes
                                (getPrimaryResource a.AssignedResources))
                        |> String.concat "\n"

                    // Write report
                    let report =
                        $"""# Drone Swarm Task Allocation Results

## Summary

- **Run ID**: {runId}
- **Method**: {methodUsed}
- **Tasks**: {tasks.Length}
- **Drones**: {drones.Length}
- **Dependencies**: {tasks |> List.sumBy (fun t -> t.DependsOn.Length)}
- **Makespan**: {solution.Makespan.TotalMinutes:F1} minutes
- **Resource Utilization**: {utilization * 100.0:F1}%%
- **Elapsed Time**: {sw.ElapsedMilliseconds} ms
- **1:N permission evidence**: {evidenceVerdict} (see `permission-evidence.md`)

## Task Schedule

| Task | Start (min) | End (min) | Duration | Resource |
|------|-------------|-----------|----------|----------|
{assignments}

## Quantum Computing Context

This example demonstrates mapping drone task allocation to **Resource-Constrained Scheduling**:

- **Classical approach**: Topological sort + greedy assignment
- **Quantum approach**: QUBO encoding + QAOA optimization

Key constraints handled:
- **Precedence**: Tasks must respect dependency order
- **Resource capacity**: Drones have limited payload capacity
- **Makespan minimization**: Complete all tasks as quickly as possible

## Files Generated

- `metrics.json` - Performance metrics
- `schedule.csv` - Task assignments with timing
- `gantt.txt` - ASCII Gantt chart visualization
- `permission-evidence.md`, `permission-evidence.json` - One-pilot-to-many (1:N) permission evidence pack
"""

                    Reporting.writeTextFile (Path.Combine(outDir, "run-report.md")) report

                    printfn "Results written to: %s" outDir
                    0
