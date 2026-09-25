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
                    | None -> Error(sprintf "row=%d invalid task type '%s'" rowNum typeStr)
                | _ -> Error(sprintf "row=%d missing or invalid task fields" rowNum))
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
                             sprintf "row=%d duplicate task_id '%s' (first on row %d); row ignored" rowNum v.Id first
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
                | _ -> Error(sprintf "row=%d missing or invalid drone fields" rowNum))
            |> List.fold
                (fun (oks, errs) r ->
                    match r with
                    | Ok v -> (v :: oks, errs)
                    | Error e -> (oks, e :: errs))
                ([], [])

        (List.rev drones, structuralErrors @ (List.rev errors))

    let readWaypoints (path: string) : Waypoint list * string list =
        if not (File.Exists path) then
            ([], [ sprintf "waypoints file not found: %s" path ])
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
            Duration = System.TimeSpan.FromMinutes effectiveDuration
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
        FinishToStart(fromTaskId, toTaskId, System.TimeSpan.Zero) // lag = 0 means tasks can start immediately after predecessor

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
        let timeHorizon = System.TimeSpan.FromMinutes(totalDuration * 2.0) // 2x buffer for scheduling flexibility

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

        sb.AppendLine("╔════════════════════════════════════════════════════════════════════════════╗")
        |> ignore

        sb.AppendLine("║  TASK SCHEDULE GANTT CHART                                                 ║")
        |> ignore

        sb.AppendLine("╠════════════════════════════════════════════════════════════════════════════╣")
        |> ignore

        // Time axis
        let maxTime = solution.Makespan.TotalMinutes
        let numTicks = min 20 (int (maxTime / timeScale))
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
                    assignment.TaskId.PadRight(12)

            sb.Append(sprintf "║ %s |" taskName) |> ignore

            for i in 0..numTicks do
                if i >= startPos && i < startPos + barLength then
                    sb.Append("███") |> ignore
                else
                    sb.Append("   ") |> ignore

            sb.AppendLine("") |> ignore

        sb.AppendLine("╚════════════════════════════════════════════════════════════════════════════╝")
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

    let private c2FadeMarginDb = 10.0
    let private sampleStepS = 1.0
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
            Samples: (float * Ev.P3)[]
            /// Payload of all the sortie's tasks, carried from launch.
            PayloadKg: float
            DistanceKm: float
            EnergyWh: float
            /// (task, seconds) where the aircraft reaches the task after its start.
            Late: (string * float) list
        }

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

    let private travelS (a: Aircraft) (p: Ev.P3) (q: Ev.P3) = Ev.dist3 p q / a.SpeedMs

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

    /// Fly one sortie: launch from the base so as to reach the first task at its
    /// start, hover over each task's interval, fly straight on to the next task
    /// (waiting there if early, recording it if late), and land at the base.
    let private sortie (a: Aircraft) (number: int) (visits: Visit list) : Sortie =
        let first = List.head visits
        let launch = first.StartS - travelS a a.Pad first.Pos

        let samples, clock, pos, late =
            visits
            |> List.fold
                (fun (samples, clock, pos, late) (v: Visit) ->
                    let arrive = clock + travelS a pos v.Pos
                    let leave = max arrive v.EndS

                    let late =
                        if arrive > v.StartS + 1e-6 then
                            (v.TaskId, arrive - v.StartS) :: late
                        else
                            late

                    ((leave, v.Pos) :: (arrive, v.Pos) :: samples, leave, v.Pos, late))
                ([ (launch, a.Pad) ], launch, a.Pad, [])

        let samples =
            (clock + travelS a pos a.Pad, a.Pad) :: samples |> List.rev |> Array.ofList

        let payload = visits |> List.sumBy (fun v -> v.PayloadKg)
        let pairs = samples |> Array.pairwise

        {
            Aircraft = a
            Number = number
            Visits = visits
            Samples = samples
            PayloadKg = payload
            DistanceKm =
                pairs
                |> Array.sumBy (fun ((_, p), (_, q)) -> Ev.dist3 p q)
                |> fun m -> m / 1000.0
            EnergyWh = pairs |> Array.sumBy (fun (s0, s1) -> segmentWh a payload s0 s1)
            Late = List.rev late
        }

    /// Split one aircraft's tasks into sorties: it lands at the base between two
    /// tasks (fresh battery, minimum ground time) whenever the gap allows the
    /// round trip, and otherwise flies straight on.
    let private sorties (a: Aircraft) (visits: Visit list) : Sortie list =
        let groundS = Scheduling.minGroundTimeMin * 60.0

        visits
        |> List.sortBy (fun v -> v.StartS)
        |> List.fold
            (fun groups (v: Visit) ->
                match groups with
                | (last :: _ as current) :: rest when
                    last.EndS + travelS a last.Pos a.Pad + groundS + travelS a a.Pad v.Pos > v.StartS
                    ->
                    (v :: current) :: rest
                | _ -> [ v ] :: groups)
            []
        |> List.rev
        |> List.mapi (fun i vs -> sortie a (i + 1) (List.rev vs))

    let label (s: Sortie) =
        sprintf "%s #%d" s.Aircraft.Drone.Id s.Number

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
        let airS = fst (Array.last s.Samples) - fst s.Samples.[0]
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
    /// Tasks go in dependency order, most important first (tasks.csv uses the
    /// scheduler convention: higher = more important). Each goes to the capable
    /// aircraft that can start it earliest while every one of that aircraft's
    /// sorties stays flyable (reached on time, within payload, range and
    /// battery) and clear of every other aircraft's tracks, waiting in steps of
    /// `waitStepS` when it must. `fixedPlan` and `doneAt` let the same function
    /// re-dispatch the leftovers of an aircraft that dropped out.
    let dispatch
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
            |> List.map (fun s ->
                ({
                    AircraftId = label s
                    Samples = s.Samples
                }
                : Ev.Track))

        let clear (ss: Sortie list) (others: Ev.Track list) =
            ss
            |> List.forall (fun s ->
                let mine =
                    ({
                        AircraftId = label s
                        Samples = s.Samples
                    }
                    : Ev.Track)

                others
                |> List.forall (fun o ->
                    match Ev.closestApproach sampleStepS groundZ [ mine; o ] with
                    | Some c -> c.Distance >= Safety.minSwarmSeparationMeters
                    | None -> true))

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
                let earliest = max depsEnd (notBefore + travelS a a.Pad v.Pos)

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
        /// minimum separations to the side of the line from the base, so a
        /// handover never puts two aircraft on one point and the watch is never
        /// left empty.
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

                    { v.Pos with
                        X = v.Pos.X + relief * nx
                        Y = v.Pos.Y + relief * ny
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

                if start + 1e-6 < max depsEnd (notBefore + travelS a a.Pad shift.Pos) then
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
                   |> List.map (fun (t, _) -> (t.Id, sprintf "%s: depends on a task nobody can fly" t.Id)))

            let ready =
                rest
                |> List.filter (fun (t: DroneTask, _) -> t.DependsOn |> List.forall doneAt.ContainsKey)
                |> List.sortBy (fun (t, _) -> (-t.Priority, t.Id))

            match ready with
            | [] ->
                (plan,
                 unassigned
                 @ (rest
                    |> List.map (fun (t, _) -> (t.Id, sprintf "%s: its dependencies never complete" t.Id))))
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

    let private separationMethod =
        sprintf
            "every flight's 3-D track (straight legs at cruise speed, hover over each task) sampled every %.0f s; pairs both below %.1f m (on the ground at the base) ignored"
            sampleStepS
            groundZ

    let private separation (o: Outcome) =
        let check =
            Ev.closestApproach sampleStepS groundZ o.Tracks
            |> Ev.Checks.separation Safety.minSwarmSeparationMeters separationMethod

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
        : Ev.Pack =
        match waypoints |> List.tryFind (fun w -> w.Id = baseId) with
        | None ->
            blocked
                "Drone swarm task allocation"
                (sprintf "base waypoint %s not found" baseId)
                [
                    sprintf "%d waypoints loaded; pass --base <waypoint id> and --waypoints <path>" waypoints.Length
                ]
                pilots
                drones.Length
        | Some origin ->
            let place = waypoints |> List.map (fun w -> (w.Id, w)) |> Map.ofList
            let taskById = tasks |> List.map (fun t -> (t.Id, t)) |> Map.ofList
            let droneIds = drones |> List.map (fun d -> d.Id) |> Set.ofList

            // --- The plan as flights ---------------------------------------------
            let placed =
                solution.Assignments
                |> List.map (fun a ->
                    match taskById.TryFind a.TaskId with
                    | None -> Error(sprintf "%s: not in tasks.csv" a.TaskId)
                    | Some t ->
                        match place.TryFind t.WaypointId with
                        | None -> Error(sprintf "%s: waypoint %s not in waypoints.csv" t.Id t.WaypointId)
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
                |> List.map (fun t -> sprintf "%s: not in the schedule" t.Id)

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
                    sprintf "%s: no usable cruise_speed_ms / max_range_km / battery_capacity_wh; not flown" d.Id)

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
                        sprintf "%s: %d tasks share this id, one schedule entry cannot cover them" id n)

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

            let pointName (w: Waypoint) = sprintf "%s %s" w.Id w.Name

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
                     |> Option.map (fun c -> sprintf "%.1f m" c.Distance)
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
                    (lost.Drone.Id, (rest, dispatch rest allWork Map.empty Map.empty 0.0)))
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
                        |> List.map (fun (w, need, have) -> sprintf "%s needs %.1f km, has %.1f" w need have))
                     @ (overTime
                        |> List.map (fun (w, need, have) -> sprintf "%s needs %.1f min, has %.1f" w need have))
                     @ (if tooClose then
                            closest
                            |> Option.map (fun c -> [ sprintf "%s / %s %.1f m at t=%.0f s" c.A c.B c.Distance c.TimeS ])
                            |> Option.defaultValue []
                        else
                            []))

            // --- In-flight dropout: the swarm absorbs it ------------------------------
            // Losing an aircraft mid-flight (lost link, low battery, a fault) is
            // expected. It holds when its fallback never comes near anyone still
            // flying (else they would have to react: a cascade), and the tasks it
            // leaves are re-dispatched to the others without a person deciding.
            let dropoutStepS = 10.0
            let rtlBaseM = 60.0
            let rtlStepM = 10.0

            let fallbackTrack (id: string) (pad: Ev.P3) (speed: float) (rtlAltM: float) (t: float) (p: Ev.P3) =
                let points = [| p; { p with Z = rtlAltM }; { pad with Z = rtlAltM }; pad |]

                ({
                    AircraftId = id
                    Samples =
                        Array.zip
                            (points
                             |> Array.pairwise
                             |> Array.scan (fun acc (a, b) -> acc + Ev.dist3 a b / speed) t)
                            points
                }
                : Ev.Track)

            let descentTrack (id: string) (t: float) (p: Ev.P3) =
                ({
                    AircraftId = id
                    Samples = [| (t, p); (t + p.Z / Safety.imuFailureDescentRateMs, { p with Z = 0.0 }) |]
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
                    let rtlAlt =
                        rtlBaseM
                        + float (fleet |> List.findIndex (fun x -> x.Drone.Id = a.Drone.Id)) * rtlStepM

                    [ launch + dropoutStepS .. dropoutStepS .. landing - 1.0 ]
                    |> List.choose (fun t ->
                        Ev.positionAt mine t
                        |> Option.map (fun p ->
                            let flownKm =
                                trackLengthKm (
                                    Array.append (s.Samples |> Array.filter (fun (ti, _) -> ti <= t)) [| (t, p) |]
                                )

                            let home = fallbackTrack (label s + " fallback") a.Pad a.SpeedMs rtlAlt t p

                            let goesHome =
                                flownKm + trackLengthKm home.Samples <= a.Drone.MaxRangeKm * usableFraction

                            let fb =
                                if goesHome then
                                    home
                                else
                                    descentTrack (label s + " descent") t p

                            let closest =
                                others
                                |> List.choose (fun o -> Ev.closestApproach sampleStepS groundZ [ fb; o ])
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
                                FallbackEndS = fst (Array.last fb.Samples)
                                FallbackTopM = fb.Samples |> Array.map (fun (_, q) -> q.Z) |> Array.max
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
                        (sprintf "%s fallback (RTL layer)" id, xs |> List.map (fun x -> x.FallbackTopM) |> List.max))

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
                        "every %.0f s along every sortie: the aircraft leaves the plan and flies its fallback (up or down to its own RTL layer, %.0f m + %.0f m per aircraft in drones.csv order, straight home, down; or a controlled descent in place when its range cannot get it home) while the others fly on; closest approach of the fallback to every other flight, and the classical dispatcher re-plans its unfinished tasks onto the others from that moment"
                        dropoutStepS
                        rtlBaseM
                        rtlStepM)
                    (sprintf
                        "%d dropout moments; closest fallback approach %s; %d knock-on conflict(s); %d leave tasks only the pilot can drop"
                        all.Length
                        (match worst with
                         | Some(x, c) ->
                             sprintf "%.1f m (%s dropping at t=%.0f s, vs %s)" c.Distance x.Aircraft x.TimeS c.B
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
                        let name = sprintf "%s drops out at t=%.0f s" x.Aircraft x.TimeS

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
                                      "controlled descent in place")
                             match x.Closest with
                             | Some c when c.Distance < Safety.minSwarmSeparationMeters ->
                                 event
                                     (sprintf "passes %.1f m from %s" c.Distance c.B)
                                     1
                                     x.TimeS
                                     Ev.decisionTimeS
                                     Ev.PilotDecision
                                     (sprintf "divert %s" c.B)
                             | _ -> ()
                             if x.Leftovers > 0 then
                                 match x.AbsorbedBy with
                                 | Some by ->
                                     event
                                         (sprintf "%d task(s) left" x.Leftovers)
                                         (List.length by)
                                         x.TimeS
                                         60.0
                                         Ev.Automatic
                                         (sprintf "dispatcher re-plans them onto %s" (String.Join(", ", by)))
                                 | None ->
                                     event
                                         (sprintf "%d task(s) left" x.Leftovers)
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
                        let name = sprintf "%s fails before launch" lost.Drone.Id

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

            {
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
                            "A watch no single aircraft can hold is flown in relief shifts, the next arriving as the previous leaves, on a station %.0f m away."
                            (2.0 * Safety.minSwarmSeparationMeters)
                        sprintf
                            "Waypoints: altitude_m is metres above ground at the waypoint; positions are projected equirectangularly around the base %s. Aircraft launch from and land at the base at ground level."
                            (pointName origin)
                        "Legs are straight 3-D lines at the aircraft's cruise_speed_ms; an aircraft launches so it reaches its first task exactly at the task's scheduled start."
                        "Every task is flown as a hover (fixed-wing: a loiter at cruise power, orbit radius not modelled) at its waypoint for its whole scheduled interval, including takeoff and return tasks whose scheduled time includes ground overheads: this over-states airborne time."
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
            }

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
                                     sprintf "base waypoint %s not in %s" baseId waypointsPath
                                 )
                             ))
                        | Some origin ->
                            let place = waypoints |> List.map (fun w -> (w.Id, w)) |> Map.ofList
                            let fleet = Evidence.fleetOf drones
                            let work = Evidence.work origin place tasks
                            let plan, unassigned = Evidence.dispatch fleet work Map.empty Map.empty 0.0

                            for _, why in unassigned do
                                printfn "⚠ %s" why

                            ("Classical dispatch (dependency order, flight-model checked)",
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
                                sprintf "%.1f" a.StartTime.TotalMinutes
                                sprintf "%.1f" a.EndTime.TotalMinutes
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
                    let evidence =
                        Evidence.build tasks drones waypoints baseId pilots methodUsed c2BandMhz dispatched solution

                    FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence.print evidence
                    FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence.write outDir evidence

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
