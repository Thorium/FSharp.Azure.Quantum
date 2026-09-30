// Copyright (c) 2025 FSharp.Azure.Quantum Contributors
// SPDX-License-Identifier: MIT

/// Dynamic behavior module for drone swarm adaptation.
/// Enables drones to self-initiate events (low battery, point of interest, etc.)
/// and the swarm to adapt formations in real-time using QAOA recalculation.
module FSharp.Azure.Quantum.Examples.Drones.SwarmChoreography.DynamicBehavior

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.QaoaCircuit
open FSharp.Azure.Quantum.Core.CircuitAbstraction

// =============================================================================
// POSITION TYPES
// =============================================================================

/// 3D position in meters (local coordinates or GPS-relative)
[<Struct>]
type Position = { X: float; Y: float; Z: float }

module Position =
    let create x y z = { X = x; Y = y; Z = z }
    let origin = { X = 0.0; Y = 0.0; Z = 0.0 }

    let distance (a: Position) (b: Position) =
        let dx = b.X - a.X
        let dy = b.Y - a.Y
        let dz = b.Z - a.Z
        sqrt (dx * dx + dy * dy + dz * dz)

// =============================================================================
// DRONE PROFILE - Type-specific thresholds and capabilities
// =============================================================================

/// Capabilities and thresholds that vary by drone type.
/// Drones use these to self-initiate events based on internal state.
type DroneProfile =
    {
        DroneType: string

        // Battery thresholds (percentage)
        BatteryLowThreshold: float // Triggers advisory
        BatteryCriticalThreshold: float // Triggers mandatory return

        // Environmental limits
        MaxWindTolerance: float // m/s
        MinOperatingTemp: float // Celsius
        MaxOperatingTemp: float // Celsius

        // Capabilities
        HasCamera: bool
        HasDropMechanism: bool
        HasLights: bool
        CanAutoRecharge: bool // Can land on charging pad
        MaxPayloadGrams: float
    }

module DroneProfile =
    /// Lightweight indoor drone (e.g., Crazyflie)
    let crazyflie =
        {
            DroneType = "Crazyflie"
            BatteryLowThreshold = 20.0
            BatteryCriticalThreshold = 10.0
            MaxWindTolerance = 5.0
            MinOperatingTemp = 0.0
            MaxOperatingTemp = 40.0
            HasCamera = false
            HasDropMechanism = false
            HasLights = true
            CanAutoRecharge = true // Crazyflie has charging pad option
            MaxPayloadGrams = 15.0
        }

    /// Standard outdoor drone (e.g., Pixhawk-based quad)
    let standard =
        {
            DroneType = "Standard"
            BatteryLowThreshold = 25.0
            BatteryCriticalThreshold = 15.0
            MaxWindTolerance = 12.0
            MinOperatingTemp = -10.0
            MaxOperatingTemp = 45.0
            HasCamera = true
            HasDropMechanism = false
            HasLights = true
            CanAutoRecharge = false
            MaxPayloadGrams = 500.0
        }

    /// Heavy-lift drone for cargo
    let heavyLifter =
        {
            DroneType = "HeavyLifter"
            BatteryLowThreshold = 30.0 // Higher threshold due to power demands
            BatteryCriticalThreshold = 20.0
            MaxWindTolerance = 8.0
            MinOperatingTemp = -5.0
            MaxOperatingTemp = 40.0
            HasCamera = true
            HasDropMechanism = true
            HasLights = true
            CanAutoRecharge = false
            MaxPayloadGrams = 2000.0
        }

    /// Scout/reconnaissance drone
    let scout =
        {
            DroneType = "Scout"
            BatteryLowThreshold = 15.0 // Optimized for endurance
            BatteryCriticalThreshold = 8.0
            MaxWindTolerance = 15.0
            MinOperatingTemp = -15.0
            MaxOperatingTemp = 50.0
            HasCamera = true
            HasDropMechanism = false
            HasLights = false // Stealth
            CanAutoRecharge = false
            MaxPayloadGrams = 100.0
        }

// =============================================================================
// EVENT TYPES - What drones can detect/initiate
// =============================================================================

/// Duration estimate for handling an event
[<Struct>]
type EventDuration =
    /// Very short, others should hover and wait (< 10 seconds)
    | Momentary of maxSeconds: float
    /// Medium duration, others may slow-loop or hover (10-60 seconds)
    | Brief of maxSeconds: float
    /// Unknown or long duration, swarm continues without this drone
    | Extended

module EventDuration =
    let isShort =
        function
        | Momentary _ -> true
        | Brief s -> s < 30.0
        | Extended -> false

    let estimatedSeconds =
        function
        | Momentary s -> s
        | Brief s -> s
        | Extended -> Double.PositiveInfinity

/// Standard drone events that can be self-initiated.
/// Each event has well-known semantics for swarm coordination.
type StandardEvent =
    // Battery/Power
    | LowBattery of currentPercent: float
    | CriticalBattery of currentPercent: float
    | RechargeComplete

    // Navigation/Safety
    | ObstacleDetected of direction: float * distance: float
    | GpsLost
    | GpsRecovered
    | ReturnToHome

    // Mission/Payload
    | PointOfInterest of coords: Position * confidence: float
    | ItemReadyToDrop
    | ItemDropped of success: bool
    | PayloadPickedUp

    // Social/Interactive
    | PersonRecognized of personId: string * coords: Position
    | GestureDetected of gestureType: string
    | FollowMeRequested of targetId: string

    // Environmental
    | HighWind of speedMs: float
    | TemperatureWarning of celsius: float
    | RainDetected

    // Hardware
    | MotorWarning of motorIndex: int * severity: float
    | SensorFault of sensorName: string
    | CommunicationDegraded of signalStrength: float

    // Formation
    | ReadyToRejoin
    | FormationPositionReached
    | CollisionRisk of otherDroneId: int * distance: float

/// Custom event for domain-specific extensions
type CustomEvent =
    {
        EventType: string
        Payload: Map<string, string>
        SuggestedDuration: EventDuration
    }

/// Union of all possible drone events
type DroneEvent =
    | Standard of StandardEvent
    | Custom of CustomEvent

module DroneEvent =
    /// Get suggested duration for standard events
    let suggestedDuration =
        function
        | Standard evt ->
            match evt with
            // Momentary events (< 10s)
            | ItemDropped _ -> Momentary 2.0
            | FormationPositionReached
            | GpsRecovered -> Momentary 1.0
            | RechargeComplete -> Momentary 5.0

            // Brief events (10-60s)
            | PointOfInterest _ -> Brief 15.0
            | PersonRecognized _ -> Brief 20.0
            | GestureDetected _ -> Brief 10.0
            | ItemReadyToDrop -> Brief 5.0
            | PayloadPickedUp -> Brief 10.0
            | ObstacleDetected _ -> Brief 5.0
            | CollisionRisk _ -> Brief 3.0

            // Extended events (unknown/long)
            | LowBattery _ -> Extended
            | CriticalBattery _ -> Extended
            | ReturnToHome
            | GpsLost -> Extended
            | HighWind _ -> Extended
            | TemperatureWarning _ -> Extended
            | RainDetected -> Extended
            | MotorWarning _ -> Extended
            | SensorFault _ -> Extended
            | CommunicationDegraded _ -> Extended
            | FollowMeRequested _ -> Extended
            | ReadyToRejoin -> Momentary 1.0

        | Custom evt -> evt.SuggestedDuration

    /// Check if event requires immediate swarm notification
    let isUrgent =
        function
        | Standard evt ->
            match evt with
            | CriticalBattery _ -> true
            | GpsLost -> true
            | CollisionRisk _ -> true
            | MotorWarning(_, severity) -> severity > 0.7
            | _ -> false
        | Custom _ -> false

    /// Check if drone will leave formation
    let causesFormationDeparture =
        function
        | Standard evt ->
            match evt with
            | LowBattery _ -> true
            | CriticalBattery _ -> true
            | ReturnToHome -> true
            | PointOfInterest _ -> true // Goes to investigate
            | FollowMeRequested _ -> true
            | HighWind _ -> true // May need to land
            | MotorWarning(_, severity) -> severity > 0.5
            | SensorFault _ -> true
            | _ -> false
        | Custom _ -> false

// =============================================================================
// COMMUNICATION PROTOCOL - Simple text-based, works with MAVLink & Crazyflie
// =============================================================================

/// Message priority for communication
type MessagePriority =
    | Low
    | Normal
    | High
    | Critical

/// Notification from drone to swarm (via ground station relay)
type SwarmNotification =
    {
        DroneId: int
        Event: DroneEvent
        CurrentPosition: Position
        Timestamp: DateTime
        Priority: MessagePriority
    }

/// Command from ground station to drone(s)
type DroneCommand =
    | Hold of maxSeconds: float
    | Resume
    | GoTo of position: Position
    | Land
    | ReturnHome
    | SetSpeed of metersPerSecond: float
    | CustomCommand of name: string * parameters: Map<string, string>

/// Command targeting specific drones or all
type SwarmCommand =
    {
        TargetDrones: int list option // None = broadcast to all
        Command: DroneCommand
        Timestamp: DateTime
    }

module Protocol =
    /// Encode notification as simple text (works with MAVLink STATUSTEXT, Crazyflie console)
    /// Format: "EVT|<drone_id>|<event_type>|<duration>|<x>|<y>|<z>|<extra>"
    let encodeNotification (n: SwarmNotification) : string =
        let eventStr, extra =
            match n.Event with
            | Standard evt ->
                match evt with
                | LowBattery pct -> "BAT_LOW", $"%.0f{pct}"
                | CriticalBattery pct -> "BAT_CRIT", $"%.0f{pct}"
                | RechargeComplete -> "RECHARGED", ""
                | ObstacleDetected(dir, dist) -> "OBSTACLE", $"%.1f{dir},%.1f{dist}"
                | GpsLost -> "GPS_LOST", ""
                | GpsRecovered -> "GPS_OK", ""
                | ReturnToHome -> "RTH", ""
                | PointOfInterest(pos, conf) -> "POI", $"%.2f{pos.X},%.2f{pos.Y},%.2f{pos.Z},%.2f{conf}"
                | ItemReadyToDrop -> "DROP_RDY", ""
                | ItemDropped ok -> "DROPPED", if ok then "1" else "0"
                | PayloadPickedUp -> "PICKED", ""
                | PersonRecognized(id, pos) -> "PERSON", $"%s{id},%.2f{pos.X},%.2f{pos.Y},%.2f{pos.Z}"
                | GestureDetected g -> "GESTURE", g
                | FollowMeRequested id -> "FOLLOW", id
                | HighWind speed -> "WIND", $"%.1f{speed}"
                | TemperatureWarning temp -> "TEMP", $"%.1f{temp}"
                | RainDetected -> "RAIN", ""
                | MotorWarning(idx, sev) -> "MOTOR", $"%d{idx},%.2f{sev}"
                | SensorFault name -> "SENSOR", name
                | CommunicationDegraded signalStrength -> "COMM", $"%.0f{signalStrength}"
                | ReadyToRejoin -> "REJOIN", ""
                | FormationPositionReached -> "POS_OK", ""
                | CollisionRisk(other, dist) -> "COLLISION", $"%d{other},%.2f{dist}"
            | Custom evt ->
                "CUSTOM:" + evt.EventType,
                evt.Payload
                |> Map.toList
                |> List.map (fun (k, v) -> k + "=" + v)
                |> String.concat ","

        let durationStr =
            match DroneEvent.suggestedDuration n.Event with
            | Momentary s -> $"M%.0f{s}"
            | Brief s -> $"B%.0f{s}"
            | Extended -> "X"

        sprintf
            "EVT|%d|%s|%s|%.2f|%.2f|%.2f|%s"
            n.DroneId
            eventStr
            durationStr
            n.CurrentPosition.X
            n.CurrentPosition.Y
            n.CurrentPosition.Z
            extra

    /// Decode notification from text
    let decodeNotification (text: string) : Result<SwarmNotification, string> =
        try
            let parts = text.Split '|'

            if parts.Length < 7 || parts.[0] <> "EVT" then
                Error "Invalid format: expected EVT|..."
            else
                let droneId = int parts.[1]
                let eventType = parts.[2]
                let durationStr = parts.[3]
                let x = float parts.[4]
                let y = float parts.[5]
                let z = float parts.[6]
                // Join remaining parts in case payload contained '|'
                let extra =
                    if parts.Length > 7 then
                        parts.[7..] |> String.concat "|"
                    else
                        ""

                let duration =
                    match durationStr with
                    | s when s.StartsWith "M" -> Momentary(float (s.Substring 1))
                    | s when s.StartsWith "B" -> Brief(float (s.Substring 1))
                    | _ -> Extended

                let tryParseFloats (s: string) = s.Split(',') |> Array.map float

                let tryGetFloat (arr: float[]) idx =
                    if idx < arr.Length then Some arr.[idx] else None

                let event =
                    match eventType with
                    // Battery/Power
                    | "BAT_LOW" -> Standard(LowBattery(float extra))
                    | "BAT_CRIT" -> Standard(CriticalBattery(float extra))
                    | "RECHARGED" -> Standard RechargeComplete

                    // Navigation/Safety
                    | "OBSTACLE" ->
                        let vals = tryParseFloats extra
                        Standard(ObstacleDetected(vals.[0], vals.[1]))
                    | "GPS_LOST" -> Standard GpsLost
                    | "GPS_OK" -> Standard GpsRecovered
                    | "RTH" -> Standard ReturnToHome

                    // Mission/Payload
                    | "POI" ->
                        let vals = tryParseFloats extra

                        let pos =
                            {
                                X = vals.[0]
                                Y = vals.[1]
                                Z = vals.[2]
                            }

                        let conf = tryGetFloat vals 3 |> Option.defaultValue 1.0
                        Standard(PointOfInterest(pos, conf))
                    | "DROP_RDY" -> Standard ItemReadyToDrop
                    | "DROPPED" -> Standard(ItemDropped(extra = "1"))
                    | "PICKED" -> Standard PayloadPickedUp

                    // Social/Interactive
                    | "PERSON" ->
                        match extra.IndexOf ',' with
                        | idx when idx > 0 ->
                            let personId = extra.Substring(0, idx)
                            let coords = tryParseFloats (extra.Substring(idx + 1))

                            let pos =
                                {
                                    X = coords.[0]
                                    Y = coords.[1]
                                    Z = coords.[2]
                                }

                            Standard(PersonRecognized(personId, pos))
                        | _ ->
                            Custom
                                {
                                    EventType = "PERSON"
                                    Payload = Map.ofList [ "raw", extra ]
                                    SuggestedDuration = duration
                                }
                    | "GESTURE" -> Standard(GestureDetected extra)
                    | "FOLLOW" -> Standard(FollowMeRequested extra)

                    // Environmental
                    | "WIND" -> Standard(HighWind(float extra))
                    | "TEMP" -> Standard(TemperatureWarning(float extra))
                    | "RAIN" -> Standard RainDetected

                    // Hardware
                    | "MOTOR" ->
                        let vals = extra.Split ','
                        Standard(MotorWarning(int vals.[0], float vals.[1]))
                    | "SENSOR" -> Standard(SensorFault extra)
                    | "COMM" -> Standard(CommunicationDegraded(float extra))

                    // Formation
                    | "REJOIN" -> Standard ReadyToRejoin
                    | "POS_OK" -> Standard FormationPositionReached
                    | "COLLISION" ->
                        let vals = extra.Split ','
                        Standard(CollisionRisk(int vals.[0], float vals.[1]))

                    // Custom events
                    | s when s.StartsWith "CUSTOM:" ->
                        let customType = s.Substring 7

                        let payload =
                            if String.IsNullOrEmpty(extra) then
                                Map.empty
                            else
                                extra.Split ','
                                |> Array.choose (fun kv ->
                                    let eqIdx = kv.IndexOf '='

                                    if eqIdx > 0 then
                                        Some(kv.Substring(0, eqIdx), kv.Substring(eqIdx + 1))
                                    else
                                        None)
                                |> Map.ofArray

                        Custom
                            {
                                EventType = customType
                                Payload = payload
                                SuggestedDuration = duration
                            }

                    // Fallback for unrecognized events
                    | _ ->
                        Custom
                            {
                                EventType = eventType
                                Payload = Map.ofList [ "raw", extra ]
                                SuggestedDuration = duration
                            }

                Ok
                    {
                        DroneId = droneId
                        Event = event
                        CurrentPosition = { X = x; Y = y; Z = z }
                        Timestamp = DateTime.UtcNow
                        Priority = if DroneEvent.isUrgent event then Critical else Normal
                    }
        with ex ->
            Error $"Parse error: %s{ex.Message}"

    /// Encode command as simple text
    /// Format: "CMD|<target>|<command>|<params>"
    let encodeCommand (cmd: SwarmCommand) : string =
        let targetStr =
            match cmd.TargetDrones with
            | None -> "*"
            | Some ids -> ids |> List.map string |> String.concat ","

        let cmdStr, cmdParams =
            match cmd.Command with
            | Hold secs -> "HOLD", $"%.0f{secs}"
            | Resume -> "RESUME", ""
            | GoTo pos -> "GOTO", $"%.2f{pos.X},%.2f{pos.Y},%.2f{pos.Z}"
            | Land -> "LAND", ""
            | ReturnHome -> "RTH", ""
            | SetSpeed mps -> "SPEED", $"%.1f{mps}"
            | CustomCommand(name, pars) ->
                "CUSTOM:" + name, pars |> Map.toList |> List.map (fun (k, v) -> k + "=" + v) |> String.concat ","

        $"CMD|%s{targetStr}|%s{cmdStr}|%s{cmdParams}"

    /// Decode command from text
    let decodeCommand (text: string) : Result<SwarmCommand, string> =
        try
            let parts = text.Split '|'

            if parts.Length < 3 || parts.[0] <> "CMD" then
                Error "Invalid format: expected CMD|..."
            else
                let targetDrones =
                    if parts.[1] = "*" then
                        None
                    else
                        Some(parts.[1].Split(',') |> Array.map int |> Array.toList)

                let cmdType = parts.[2]
                let cmdParams = if parts.Length > 3 then parts.[3] else ""

                let command =
                    match cmdType with
                    | "HOLD" -> Hold(if cmdParams = "" then 30.0 else float cmdParams)
                    | "RESUME" -> Resume
                    | "GOTO" ->
                        let coords = cmdParams.Split(',') |> Array.map float

                        GoTo
                            {
                                X = coords.[0]
                                Y = coords.[1]
                                Z = coords.[2]
                            }
                    | "LAND" -> Land
                    | "RTH" -> ReturnHome
                    | "SPEED" -> SetSpeed(float cmdParams)
                    | s when s.StartsWith "CUSTOM:" ->
                        let name = s.Substring 7

                        let pars =
                            if String.IsNullOrEmpty(cmdParams) then
                                Map.empty
                            else
                                cmdParams.Split ','
                                |> Array.choose (fun kv ->
                                    match kv.Split '=' with
                                    | [| k; v |] -> Some(k, v)
                                    | _ -> None)
                                |> Map.ofArray

                        CustomCommand(name, pars)
                    | _ -> CustomCommand(cmdType, Map.empty)

                Ok
                    {
                        TargetDrones = targetDrones
                        Command = command
                        Timestamp = DateTime.UtcNow
                    }
        with ex ->
            Error $"Parse error: %s{ex.Message}"

// =============================================================================
// SWARM ADAPTATION - Handle drone departures and rejoins
// =============================================================================

/// State of a drone in the swarm
type DroneState =
    | Active
    | Holding
    | Departed of reason: DroneEvent * departureTime: DateTime
    | Returning
    | Offline

/// Formation with assigned drone positions
type Formation =
    {
        Name: string
        Positions: Position array
    }

/// Current swarm state
type SwarmState =
    {
        DroneStates: Map<int, DroneState>
        DronePositions: Map<int, Position>
        DroneProfiles: Map<int, DroneProfile>
        CurrentFormation: Formation option
        FormationQueue: Formation list
        IsHolding: bool
        HoldStartTime: DateTime option
    }

module SwarmState =
    /// Create swarm state. If fewer profiles than drones, uses DroneProfile.standard for missing ones.
    let create (droneCount: int) (profiles: DroneProfile list) =
        let profileMap =
            List.init (FSharp.Core.Operators.max 0 droneCount) (fun i ->
                let profile =
                    profiles |> List.tryItem i |> Option.defaultValue DroneProfile.standard

                i, profile)
            |> Map.ofList

        {
            DroneStates =
                List.init (FSharp.Core.Operators.max 0 droneCount) (fun i -> i, Active)
                |> Map.ofList
            DronePositions = Map.empty
            DroneProfiles = profileMap
            CurrentFormation = None
            FormationQueue = []
            IsHolding = false
            HoldStartTime = None
        }

    let activeDrones (state: SwarmState) =
        state.DroneStates
        |> Map.toList
        |> List.choose (fun (id, s) ->
            match s with
            | Active
            | Holding -> Some id
            | _ -> None)

    let departedDrones (state: SwarmState) =
        state.DroneStates
        |> Map.toList
        |> List.choose (fun (id, s) ->
            match s with
            | Departed(evt, time) -> Some(id, evt, time)
            | _ -> None)

/// Attempts of a fixed-angle QAOA run whose samples hold no acceptable solution: the same
/// angles with four times the shots, then two other angle sets at four times the shots. When
/// every attempt fails the caller gets an Error; no classical solver stands in.
module QaoaAttempts =

    /// (shots, angles) per attempt, in order.
    let schedule (shots: int) (angles: (float * float)[]) : (int * (float * float)[]) list =
        [
            shots, angles
            4 * shots, angles
            4 * shots, angles |> Array.map (fun (gamma, beta) -> (1.6 * gamma, 1.3 * beta))
            4 * shots, angles |> Array.map (fun (gamma, beta) -> (0.6 * gamma, 0.7 * beta))
        ]

    /// The first attempt's accepted solution and a description of that attempt. `attempt`
    /// runs the circuit and returns the accepted solution its samples hold, if any; an
    /// execution Error stops at once.
    let firstSolved
        (shots: int)
        (angles: (float * float)[])
        (attempt: int -> (float * float)[] -> Result<'T option, string>)
        : Result<'T * string, string> =
        let attempts = schedule shots angles

        let describe (s: int) (a: (float * float)[]) =
            a
            |> Array.map (fun (gamma, beta) -> sprintf "γ=%.3f β=%.3f" gamma beta)
            |> String.concat ", "
            |> sprintf "%d shots, %s" s

        let rec go (index: int) (remaining: (int * (float * float)[]) list) =
            match remaining with
            | [] ->
                Error(
                    sprintf
                        "no QAOA sample gave an acceptable solution in %d attempts (%s)"
                        attempts.Length
                        (attempts |> List.map (fun (s, a) -> describe s a) |> String.concat "; ")
                )
            | (s, a) :: rest ->
                match attempt s a with
                | Error err -> Error err
                | Ok(Some solution) -> Ok(solution, sprintf "attempt %d of %d: %s" index attempts.Length (describe s a))
                | Ok None -> go (index + 1) rest

        go 1 attempts

/// Result of swarm adaptation calculation
type AdaptationResult =
    {
        /// New assignments: drone ID -> position index in SELECTED positions
        Assignments: Map<int, int>
        /// Selected position indices from original formation (maps local index to original index)
        SelectedPositions: int[]
        /// Drones that should hold position
        HoldingDrones: int list
        /// Drones that have left formation
        DepartedDrones: int list
        /// Whether QAOA produced the assignments (false only with no active drones)
        UsedQuantum: bool
        /// Time taken to compute
        ComputeTimeMs: int64
        /// Method description
        Method: string
        /// Computation generation (for staleness detection)
        Generation: int64
        /// Whether computation was cancelled
        WasCancelled: bool
    }

module SwarmAdaptation =
    open System.Diagnostics
    open System.Threading

    /// Large distance used when drone position is unknown
    [<Literal>]
    let private UnknownPositionDistance = 1000.0

    /// Maximum problem size for QAOA (n drones * n positions = n² qubits)
    /// For n=4 drones: 16 qubits. For n=5: 25 qubits (too large).
    /// Effective limit: 4 drones; wider problems are an Error.
    [<Literal>]
    let private MaxQaoaQubits = 20

    /// QAOA depth (number of layers) - higher = better quality, slower
    [<Literal>]
    let private QaoaDepth = 2

    /// Mutable generation counter for tracking computation staleness
    /// Mutable generation counter for thread-safe access via Interlocked
    /// Note: mutable + Interlocked is the standard F# pattern for lock-free counters
    let mutable private generationCounter = 0L

    /// Increment and get next generation number (thread-safe)
    let nextGeneration () =
        Interlocked.Increment(&generationCounter)

    /// Get current generation without incrementing
    let getGeneration () = Interlocked.Read(&generationCounter)

    /// Select best N positions from formation for N active drones
    /// Uses greedy selection based on minimum total distance from drone centroid
    let selectPositions (currentPositions: Map<int, Position>) (formation: Formation) (droneCount: int) : int[] =
        let allPositions = formation.Positions

        if droneCount >= allPositions.Length then
            // Need all positions (or more drones than positions)
            [| 0 .. allPositions.Length - 1 |]
        else
            // Compute centroid of active drones
            let dronePositions = currentPositions |> Map.toList |> List.map snd

            let centroid =
                if dronePositions.IsEmpty then
                    Position.origin
                else
                    let sumX = dronePositions |> List.sumBy (fun p -> p.X)
                    let sumY = dronePositions |> List.sumBy (fun p -> p.Y)
                    let sumZ = dronePositions |> List.sumBy (fun p -> p.Z)
                    let n = float dronePositions.Length

                    {
                        X = sumX / n
                        Y = sumY / n
                        Z = sumZ / n
                    }

            // Select N positions closest to centroid
            allPositions
            |> Array.indexed
            |> Array.sortBy (fun (_, pos) -> Position.distance centroid pos)
            |> Array.take droneCount
            |> Array.map fst

    /// Build distance matrix from current positions to SELECTED target positions
    let buildDistanceMatrix
        (currentPositions: Map<int, Position>)
        (formation: Formation)
        (selectedIndices: int[])
        (droneIds: int list)
        : float[,] =

        let selectedPositions =
            selectedIndices |> Array.map (fun i -> formation.Positions.[i])

        let m = selectedPositions.Length

        droneIds
        |> List.map (fun droneId ->
            match Map.tryFind droneId currentPositions with
            | Some pos ->
                selectedPositions
                |> Array.map (fun targetPos -> Position.distance pos targetPos)
            | None ->
                // Drone position unknown, use large distance for all positions
                Array.create m UnknownPositionDistance)
        |> array2D

    /// Encode drone-position assignment as QUBO matrix
    ///
    /// Variables: x[i,j] = 1 if drone i assigned to position j
    /// For n drones and m positions, we have n*m binary variables.
    ///
    /// Objective: minimize total travel distance
    ///   min Σ_i Σ_j d[i,j] * x[i,j]
    ///
    /// Constraints (encoded as penalties):
    /// 1. Each drone assigned exactly once: Σ_j x[i,j] = 1 for all i
    /// 2. Each position assigned at most once: Σ_i x[i,j] ≤ 1 for all j
    ///    (For n < m, some positions remain empty)
    let encodeAssignmentQubo (distanceMatrix: float[,]) : float[,] =
        let n = Array2D.length1 distanceMatrix // number of drones
        let m = Array2D.length2 distanceMatrix // number of positions
        let numVars = n * m

        // Variable index: drone i, position j -> i * m + j
        let varIndex i j = i * m + j

        // Compute penalty weight using Lucas Rule
        let maxDistance =
            [|
                for i in 0 .. n - 1 do
                    for j in 0 .. m - 1 do
                        yield distanceMatrix.[i, j]
            |]
            |> Array.max

        let penalty = Qubo.computeLucasPenalties maxDistance (max n m)

        // Initialize QUBO matrix
        let qubo = Array2D.zeroCreate<float> numVars numVars

        // Objective: minimize distance (diagonal terms in QUBO)
        // QUBO minimizes, so we use positive coefficients for distances
        for i in 0 .. n - 1 do
            for j in 0 .. m - 1 do
                let idx = varIndex i j
                qubo.[idx, idx] <- distanceMatrix.[i, j]

        // Constraint 1: Each drone assigned exactly once
        // Penalty: λ * (Σ_j x[i,j] - 1)² for each drone i
        // Expands to: λ * (Σ_j x[i,j]² - 2*Σ_j x[i,j] + 2*Σ_{j<k} x[i,j]*x[i,k] + 1)
        // QUBO form: -λ on diagonal, +2λ on off-diagonal pairs for same drone
        for i in 0 .. n - 1 do
            // Diagonal terms: -penalty (encourages selection)
            for j in 0 .. m - 1 do
                let idx = varIndex i j
                qubo.[idx, idx] <- qubo.[idx, idx] - penalty

            // Off-diagonal terms: +2*penalty (discourages multiple selections)
            for j1 in 0 .. m - 1 do
                for j2 in j1 + 1 .. m - 1 do
                    let idx1 = varIndex i j1
                    let idx2 = varIndex i j2
                    qubo.[idx1, idx2] <- qubo.[idx1, idx2] + 2.0 * penalty
                    qubo.[idx2, idx1] <- qubo.[idx2, idx1] + 2.0 * penalty

        // Constraint 2: Each position assigned at most once (for n ≤ m case)
        // Using soft constraint with lower penalty (positions can be empty)
        let softPenalty = penalty * 0.5

        for j in 0 .. m - 1 do
            for i1 in 0 .. n - 1 do
                for i2 in i1 + 1 .. n - 1 do
                    let idx1 = varIndex i1 j
                    let idx2 = varIndex i2 j
                    qubo.[idx1, idx2] <- qubo.[idx1, idx2] + 2.0 * softPenalty
                    qubo.[idx2, idx1] <- qubo.[idx2, idx1] + 2.0 * softPenalty

        qubo

    /// Decode QAOA measurement result to assignment
    /// Returns None if the solution violates constraints
    let decodeAssignment (bitstring: string) (n: int) (m: int) (droneIds: int list) : Map<int, int> option =
        if bitstring.Length <> n * m then
            None
        else
            // Parse bitstring to assignment
            let assignments =
                droneIds
                |> List.indexed
                |> List.choose (fun (i, droneId) ->
                    // Find which position this drone is assigned to
                    let assignedPositions =
                        [ 0 .. m - 1 ]
                        |> List.filter (fun j ->
                            let idx = i * m + j
                            bitstring.[idx] = '1')

                    match assignedPositions with
                    | [ j ] -> Some(droneId, j) // Valid: exactly one position
                    | _ -> None) // Invalid: zero or multiple positions

            // Check if all drones got assigned
            if assignments.Length = droneIds.Length then
                // Check for position conflicts
                let positions = assignments |> List.map snd
                let uniquePositions = positions |> Set.ofList

                if uniquePositions.Count = positions.Length then
                    Some(Map.ofList assignments)
                else
                    None // Position conflict
            else
                None // Not all drones assigned


    /// Run QAOA to find an assignment: the most frequent sampled bitstring that decodes to a
    /// valid assignment `accept` takes (local position indices), retried by QaoaAttempts. No
    /// valid, accepted sample in any attempt, or a problem wider than MaxQaoaQubits, is an
    /// Error; no classical solver stands in. Returns the assignment and the attempt that
    /// found it.
    let qaoaAssignment
        (backend: IQuantumBackend)
        (shots: int)
        (accept: Map<int, int> -> bool)
        (distanceMatrix: float[,])
        (droneIds: int list)
        : Result<Map<int, int> * string, string> =

        let n = Array2D.length1 distanceMatrix
        let m = Array2D.length2 distanceMatrix
        let numQubits = n * m

        // Check if problem size is within QAOA limits
        if numQubits > MaxQaoaQubits then
            Error $"problem too large for QAOA (%d{numQubits} qubits > %d{MaxQaoaQubits} max)"
        else
            // Encode as QUBO
            let quboMatrix = encodeAssignmentQubo distanceMatrix

            // Convert QUBO to Problem Hamiltonian, normalised so the fixed angles are on the
            // scale the library's solvers use
            let problemHam =
                ProblemHamiltonian.fromQubo quboMatrix |> ProblemHamiltonian.normalize

            let mixerHam = MixerHamiltonian.create numQubits

            // Initial QAOA parameters (heuristic starting point)
            // gamma ~ π/4, beta ~ π/8 are reasonable starting values
            let parameters = Array.init QaoaDepth (fun _ -> (Math.PI / 4.0, Math.PI / 8.0))

            let attempt (attemptShots: int) (angles: (float * float)[]) =
                // Wrap QAOA circuit for backend execution via ICircuit interface
                let wrappedCircuit = wrapQaoaCircuit (QaoaCircuit.build problemHam mixerHam angles)

                match backend.ExecuteToState wrappedCircuit with
                | Error err -> Error err.Message
                | Ok quantumState ->
                    // Most frequent valid, accepted outcome first
                    QuantumState.measure quantumState attemptShots
                    |> Array.map (Array.map string >> String.concat "")
                    |> Array.countBy id
                    |> Array.sortByDescending snd
                    |> Array.tryPick (fun (bitstring, _count) ->
                        decodeAssignment bitstring n m droneIds |> Option.filter accept)
                    |> Ok

            QaoaAttempts.firstSolved shots parameters attempt

    /// Adapt formation when drone(s) depart: select as many positions as there are active
    /// drones and assign them by QAOA (qaoaAssignment), taking only an assignment `accept`
    /// takes (drone ID -> original formation position index). An Error when QAOA finds none,
    /// the problem is too wide for QAOA, or the time budget is under 100 ms.
    let adaptFormationWith
        (backend: IQuantumBackend)
        (shots: int)
        (accept: Map<int, int> -> bool)
        (state: SwarmState)
        (targetFormation: Formation)
        (maxComputeTimeMs: int64)
        : Result<AdaptationResult, string> =

        let sw = Stopwatch.StartNew()
        let generation = nextGeneration () // Track computation generation
        let activeDroneIds = SwarmState.activeDrones state

        let departedDroneIds =
            SwarmState.departedDrones state |> List.map (fun (id, _, _) -> id)

        if activeDroneIds.IsEmpty then
            sw.Stop()

            Ok
                {
                    Assignments = Map.empty
                    SelectedPositions = [||]
                    HoldingDrones = []
                    DepartedDrones = departedDroneIds
                    UsedQuantum = false
                    ComputeTimeMs = sw.ElapsedMilliseconds
                    Method = "NoActiveDrones"
                    Generation = generation
                    WasCancelled = false
                }
        elif maxComputeTimeMs < 100L then
            Error $"time budget too small for QAOA (%d{maxComputeTimeMs} ms)"
        else
            let n = activeDroneIds.Length

            // Select optimal positions for the number of active drones
            // This reduces qubit count from n*m to n*n when m > n
            let selectedIndices = selectPositions state.DronePositions targetFormation n
            let m = selectedIndices.Length // Now m = n (square problem)
            let numQubits = n * m

            let distanceMatrix =
                buildDistanceMatrix state.DronePositions targetFormation selectedIndices activeDroneIds

            // Map local position indices back to original formation indices
            let toOriginal (local: Map<int, int>) =
                local
                |> Map.map (fun _droneId localPosIdx -> Array.item localPosIdx selectedIndices)

            qaoaAssignment backend shots (toOriginal >> accept) distanceMatrix activeDroneIds
            |> Result.map (fun (localAssignments, how) ->
                sw.Stop()

                {
                    Assignments = toOriginal localAssignments
                    SelectedPositions = selectedIndices
                    HoldingDrones = []
                    DepartedDrones = departedDroneIds
                    UsedQuantum = true
                    ComputeTimeMs = sw.ElapsedMilliseconds
                    Method = $"QAOA (p=%d{QaoaDepth}, %d{numQubits} qubits, %s{how})"
                    Generation = generation
                    WasCancelled = false
                })

    /// adaptFormationWith taking any valid assignment.
    let adaptFormation
        (backend: IQuantumBackend)
        (shots: int)
        (state: SwarmState)
        (targetFormation: Formation)
        (maxComputeTimeMs: int64)
        : Result<AdaptationResult, string> =
        adaptFormationWith backend shots (fun _ -> true) state targetFormation maxComputeTimeMs

// =============================================================================
// EVENT HANDLER - Process notifications and generate commands
// =============================================================================

/// Configuration for event handling
[<Struct>]
type EventHandlerConfig =
    {
        /// Max time to wait for short events before continuing
        MaxHoldTimeSeconds: float
        /// Max time to compute new assignments
        MaxComputeTimeMs: int64
        /// Whether to use quantum optimization
        UseQuantum: bool
        /// Shots for QAOA
        QaoaShots: int
    }

module EventHandlerConfig =
    let defaults =
        {
            MaxHoldTimeSeconds = 30.0
            MaxComputeTimeMs = 5000L
            UseQuantum = true
            QaoaShots = 1000
        }

/// Handle incoming drone notification
/// Note: backend parameter reserved for future QAOA-based decision making
let handleNotification
    (_backend: IQuantumBackend)
    (config: EventHandlerConfig)
    (state: SwarmState)
    (notification: SwarmNotification)
    : SwarmState * SwarmCommand list =

    let droneId = notification.DroneId
    let event = notification.Event
    let duration = DroneEvent.suggestedDuration event

    // Update drone position
    let stateWithPosition =
        { state with
            DronePositions = Map.add droneId notification.CurrentPosition state.DronePositions
        }

    // Check if this is a ReadyToRejoin event
    let isReadyToRejoin =
        match event with
        | Standard ReadyToRejoin -> true
        | _ -> false

    // Determine response based on event type and duration
    if DroneEvent.causesFormationDeparture event then
        // Drone is leaving formation
        let newDroneStates =
            Map.add droneId (Departed(event, DateTime.UtcNow)) stateWithPosition.DroneStates

        let newState =
            { stateWithPosition with
                DroneStates = newDroneStates
            }

        match duration with
        | Momentary secs
        | Brief secs when secs < config.MaxHoldTimeSeconds ->
            // Short event: tell others to hold, let this drone do its thing
            let otherDrones = SwarmState.activeDrones newState |> List.filter ((<>) droneId)

            let holdCmd =
                {
                    TargetDrones = Some otherDrones
                    Command = Hold secs
                    Timestamp = DateTime.UtcNow
                }

            { newState with
                IsHolding = true
                HoldStartTime = Some DateTime.UtcNow
            },
            [ holdCmd ]

        | _ ->
            // Long event: continue without this drone
            // Formation will be recalculated on next transition
            newState, []

    elif isReadyToRejoin then
        // Drone wants to rejoin
        let newDroneStates = Map.add droneId Returning stateWithPosition.DroneStates

        let newState =
            { stateWithPosition with
                DroneStates = newDroneStates
            }

        // It will be included in next formation calculation
        newState, []

    else
        // Informational event, no formation change needed
        stateWithPosition, []

/// Resume swarm after hold period
/// Note: backend parameter reserved for future QAOA-based replanning on resume
let resumeSwarm
    (_backend: IQuantumBackend)
    (_config: EventHandlerConfig)
    (state: SwarmState)
    : SwarmState * SwarmCommand list =

    let resumeCmd =
        {
            TargetDrones = None // Broadcast to all
            Command = Resume
            Timestamp = DateTime.UtcNow
        }

    { state with
        IsHolding = false
        HoldStartTime = None
    },
    [ resumeCmd ]
