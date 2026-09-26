/// Air bridge: domain types, local geometry and CSV parsing.
///
/// The planner works on a local flat plane in kilometres (x = east, y =
/// north). Where the plane comes from is the FRAME: geodetic input
/// (latitude/longitude, projected around the centroid; the scenario spans a
/// few km, so an equirectangular projection is accurate to well under a metre)
/// or a local frame already in metres (a room, a field), used as it is.
///
/// Time is counted in TICKS of one minute or one second, so the same model
/// runs a forest fire over an hour or a demo over ninety seconds.
module FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.FireDomain

open System
open System.Globalization

open FSharp.Azure.Quantum.Examples.Common
open FSharp.Azure.Quantum.Examples.Drones.Domain

// =============================================================================
// FRAME AND TICK
// =============================================================================

/// Where positions come from.
type Frame =
    /// `latitude` / `longitude` columns, projected onto local km around the
    /// centroid of all points. The pilot station is the centroid.
    | Geodetic
    /// `x_m` / `y_m` columns: a local flat frame in metres, used as it is.
    /// The pilot station is its origin.
    | LocalMetres

module Frame =
    let name =
        function
        | Geodetic -> "geodetic (latitude/longitude, projected)"
        | LocalMetres -> "local metres"

/// A position as read from a CSV row, before the frame is known.
type RawPos =
    | LatLon of lat: float * lon: float
    | Metres of x: float * y: float

/// The simulation's unit of time. Rates, horizons, latencies and event times
/// are all counted in ticks.
type Tick =
    | Minute
    | Second

module Tick =
    let seconds =
        function
        | Minute -> 60.0
        | Second -> 1.0

    /// Short unit name for labels ("min", "s").
    let unit =
        function
        | Minute -> "min"
        | Second -> "s"

    let name =
        function
        | Minute -> "minute"
        | Second -> "second"

    let tryParse (s: string) =
        match s.Trim().ToLowerInvariant() with
        | "minute"
        | "minutes"
        | "min" -> Some Minute
        | "second"
        | "seconds"
        | "s"
        | "sec" -> Some Second
        | _ -> None

// =============================================================================
// GEOMETRY
// =============================================================================

/// Local planar position in km (X = east, Y = north).
type Pos = { X: float; Y: float }

module Geometry =

    let distanceKm (a: Pos) (b: Pos) =
        Math.Sqrt((b.X - a.X) ** 2.0 + (b.Y - a.Y) ** 2.0)

    /// Unit vector from a to b (zero vector when a = b).
    let unit (a: Pos) (b: Pos) : Pos =
        let d = distanceKm a b

        if d < 1e-9 then
            { X = 0.0; Y = 0.0 }
        else
            {
                X = (b.X - a.X) / d
                Y = (b.Y - a.Y) / d
            }

    /// Unit vector of a compass bearing (0 = north, 90 = east).
    let bearing (degrees: float) : Pos =
        let r = degrees * Math.PI / 180.0
        { X = Math.Sin r; Y = Math.Cos r }

    let dot (a: Pos) (b: Pos) = a.X * b.X + a.Y * b.Y

    let private cross (o: Pos) (a: Pos) (b: Pos) =
        (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X)

    /// True when segments p1-p2 and q1-q2 properly cross. Segments that only
    /// share an endpoint (two lanes out of the same lake) do not count.
    let segmentsCross (p1: Pos) (p2: Pos) (q1: Pos) (q2: Pos) =
        let d1 = cross q1 q2 p1
        let d2 = cross q1 q2 p2
        let d3 = cross p1 p2 q1
        let d4 = cross p1 p2 q2
        d1 * d2 < 0.0 && d3 * d4 < 0.0

    /// Shortest distance from point p to segment a-b.
    let pointSegmentKm (p: Pos) (a: Pos) (b: Pos) =
        let dx, dy = b.X - a.X, b.Y - a.Y
        let len2 = dx * dx + dy * dy

        let t =
            if len2 < 1e-12 then
                0.0
            else
                Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0.0, 1.0)

        distanceKm p { X = a.X + t * dx; Y = a.Y + t * dy }

    /// Shortest horizontal distance between two segments: 0 when they cross,
    /// and exact for overlapping or touching ones (for segments that do not
    /// cross, the minimum is always at one of the four endpoints).
    let segmentDistanceKm (p1: Pos) (p2: Pos) (q1: Pos) (q2: Pos) =
        if segmentsCross p1 p2 q1 q2 then
            0.0
        else
            [
                pointSegmentKm p1 q1 q2
                pointSegmentKm p2 q1 q2
                pointSegmentKm q1 p1 p2
                pointSegmentKm q2 p1 p2
            ]
            |> List.min

// =============================================================================
// DOMAIN TYPES
// =============================================================================

/// Where drones load up between drops: a lake, river or reservoir for water,
/// or the launch pad of a demo where a "fill" is the turnaround hover. Its
/// fill slots are the bottleneck most often: a lake with 4 slots and a 25 s
/// fill passes at most 4 * 60 / 25 = 9.6 drones per minute, however many
/// drones are sent there.
type WaterSource =
    {
        Id: string
        Name: string
        Pos: Pos
        FillSlots: int
        FillTimeS: float
    }

/// A target of the air bridge: a patch of the fire front (or unburnt forest
/// next to it), or a thing to be touched in a demo. Which of its fields
/// matter is the demand model's business.
type FireSector =
    {
        Id: string
        Name: string
        Pos: Pos
        AreaHa: float
        InitialIntensity: float
        /// Value of what the sector protects (1 = forest, 3 = cabins).
        AssetPriority: float
        /// Drones that can drop at the same time without spacing conflicts.
        DropSlots: int
        /// Drops the target needs (touch demand).
        Touches: int
        Neighbors: string list
    }

/// One class of drone. The fleet is homogeneous in this example.
type DroneClass =
    {
        Model: string
        Count: int
        /// Litres per drop; 0 when the drops carry nothing (touch demand).
        PayloadL: float
        LoadedSpeedMs: float
        EmptySpeedMs: float
        EnduranceMin: float
        DropTimeS: float
        /// Time a battery swap takes at the source, seconds.
        SwapTimeS: float
        /// In-trail spacing on a lane; sets the lane's headway capacity.
        LaneSpacingM: float
        /// Altitude of the lowest outbound lane layer.
        LaneBaseAltM: float
        /// Vertical step between altitude layers of lanes that would cross.
        LaneStepM: float
        /// Height of a corridor's return lane above its outbound lane.
        ReturnOffsetM: float
        /// Least horizontal separation the evidence accepts (in-trail minimum).
        MinSeparationM: float
        /// Least vertical separation the evidence accepts between lanes.
        MinVerticalM: float
    }

/// The terminal geometry shared by the corridor model and the dispatcher: what
/// an aircraft flies at each end of a lane besides the lane itself. The flow
/// model must count it, or it plans cycles no whole aircraft can fly.
module Terminal =

    /// Vertical speeds the missions assume (ArduPilot's WPNAV_SPEED_UP/DN).
    let climbMs (fleet: DroneClass) = min 2.5 fleet.LoadedSpeedMs
    let descentMs (fleet: DroneClass) = min 1.5 fleet.LoadedSpeedMs

    /// ArduPilot lands the last `landAltLowM` metres at LAND_SPEED, slower
    /// than the descent above it; the tracks and the battery count it.
    let landSpeedMs = 0.5
    let landAltLowM = 10.0

    /// Seconds to descend from `altM` onto the pad.
    let landingS (fleet: DroneClass) (altM: float) =
        max 0.0 (altM - landAltLowM) / descentMs fleet
        + min altM landAltLowM / landSpeedMs

    /// Seconds an aircraft is on the ground between two sorties before the
    /// next can start: ArduPilot's disarm delay, then the launcher's upload,
    /// read-back, arm and start. Written to the .parm as DISARM_DELAY.
    let disarmDelayS = 5.0
    let turnaroundS = disarmDelayS + 10.0

    /// Drop slots on an arc around the target that leaves the lane's approach
    /// sector free: slot 0 straight on from the lane, the others fanned to
    /// both sides in steps of 2 pi / (n + 1), so no slot lies on the inbound
    /// lane. Returns (radius, angular step). The radius keeps neighbouring
    /// slots one `spacing` apart, every slot one spacing from the centre, and
    /// the radial into a slot one spacing from the aircraft hovering in the
    /// next one (radius x sin step), since the radials converge.
    let dropRing (slots: int) (spacing: float) =
        if slots <= 1 then
            (spacing, 0.0)
        else
            let step = 2.0 * Math.PI / float (slots + 1)
            let chord = spacing / (2.0 * Math.Sin(step / 2.0))
            let radial = if step < Math.PI / 2.0 then spacing / Math.Sin step else 0.0
            (max spacing (max chord radial), step)

    /// Radius of a ring of `slots` points one `spacing` apart (the fill slots
    /// of the flow model's terminal check).
    let ringRadius (slots: int) (spacing: float) =
        if slots <= 1 then
            spacing
        else
            max spacing (spacing / (2.0 * Math.Sin(Math.PI / float slots)))

    /// Pads on an arc of up to a half circle around a source: (radius, angular
    /// step). Neighbouring pads are one `separation` apart, and an aircraft
    /// flying its radial leg in to the centre stays a separation from every
    /// other pad (radius x sin step), because the radials converge.
    let padRing (n: int) (separation: float) (spacing: float) =
        if n <= 1 then
            (spacing, 0.0)
        else
            let step = Math.PI / float (n - 1)
            let chord = separation / (2.0 * Math.Sin(step / 2.0))
            let radial = if step < Math.PI / 2.0 then separation / Math.Sin step else 0.0
            (max spacing (max chord radial), step)

    /// Seconds a cycle spends outside the lane: climb from the pad, the radial
    /// leg to the source centre, the leg to the drop slot and back, the climb
    /// to the return altitude, the radial leg back over the pad and the
    /// descent onto it. Split into the outbound share (before the first drop)
    /// and the rest.
    let overheadS (fleet: DroneClass) (padRadiusM: float) (dropRadiusM: float) =
        let outbound =
            fleet.LaneBaseAltM / climbMs fleet
            + padRadiusM / fleet.LoadedSpeedMs
            + dropRadiusM / fleet.LoadedSpeedMs

        let back =
            fleet.ReturnOffsetM / climbMs fleet
            + dropRadiusM / fleet.EmptySpeedMs
            + padRadiusM / fleet.EmptySpeedMs
            + landingS fleet (fleet.LaneBaseAltM + fleet.ReturnOffsetM)

        (outbound, back)

type ScenarioEvent =
    /// Direction the wind blows TOWARD, compass degrees.
    | WindDirection of toDeg: float
    | WindSpeed of ms: float
    | CloseSource of sourceId: string
    | OpenSource of sourceId: string
    /// New demand at a target: a spot fire's intensity, or touches to add.
    | SpotFire of sectorId: string * value: float
    | LoseDrones of count: int

/// `At` is in ticks.
type TimedEvent = { At: int; Event: ScenarioEvent }

// =============================================================================
// CSV PARSING
// =============================================================================

module Parse =

    let private tryGet (k: string) (row: Data.CsvRow) =
        row.Values
        |> Map.tryFind k
        |> Option.map (fun s -> s.Trim())
        |> Option.filter (String.IsNullOrWhiteSpace >> not)

    // InvariantCulture: coordinates must parse the same on a comma-decimal locale.
    let private tryFloat (s: string option) =
        s
        |> Option.bind (fun v ->
            match Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture) with
            | true, x -> Some x
            | false, _ -> None)

    let private tryInt (s: string option) =
        s
        |> Option.bind (fun v ->
            match Int32.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture) with
            | true, x -> Some x
            | false, _ -> None)

    /// An optional numeric column: the default when absent, an error when
    /// present but unparsable or outside `ok`.
    let private floatOr (k: string) (fallback: float) (ok: float -> bool) (row: Data.CsvRow) =
        match tryGet k row with
        | None -> Ok fallback
        | Some v ->
            match tryFloat (Some v) with
            | Some x when ok x -> Ok x
            | _ -> Error k

    let private intOr (k: string) (fallback: int) (ok: int -> bool) (row: Data.CsvRow) =
        match tryGet k row with
        | None -> Ok fallback
        | Some v ->
            match tryInt (Some v) with
            | Some x when ok x -> Ok x
            | _ -> Error k

    /// All Ok values, or the first Error's column name.
    let private allOk (rs: Result<float, string> list) : Result<float list, string> =
        rs
        |> List.fold
            (fun acc r ->
                match acc, r with
                | Ok xs, Ok x -> Ok(x :: xs)
                | Error e, _ -> Error e
                | _, Error e -> Error e)
            (Ok [])
        |> Result.map List.rev

    let private collect (rows: Result<'T, string> list) : 'T list * string list =
        let oks =
            rows
            |> List.choose (function
                | Ok v -> Some v
                | Error _ -> None)

        let errs =
            rows
            |> List.choose (function
                | Error e -> Some e
                | Ok _ -> None)

        (oks, errs)

    let private readRows (path: string) (parseRow: int -> Data.CsvRow -> Result<'T, string>) =
        let rows, structural = Data.readCsvWithHeaderWithErrors path
        let items, errors = rows |> List.mapi (fun i row -> parseRow (i + 2) row) |> collect
        (items, structural @ errors)

    /// A row's position: geodetic columns, or local metres.
    let private rawPos (row: Data.CsvRow) =
        match
            tryFloat (tryGet "latitude" row),
            tryFloat (tryGet "longitude" row),
            tryFloat (tryGet "x_m" row),
            tryFloat (tryGet "y_m" row)
        with
        | Some lat, Some lon, _, _ -> Some(LatLon(lat, lon))
        | _, _, Some x, Some y -> Some(Metres(x, y))
        | _ -> None

    /// Sources and sectors carry their raw position until the frame is known.
    let readSources (path: string) =
        readRows path (fun rowNum row ->
            match
                tryGet "source_id" row,
                tryGet "name" row,
                rawPos row,
                tryInt (tryGet "fill_slots" row),
                tryFloat (tryGet "fill_time_s" row)
            with
            | Some id, Some name, Some pos, Some slots, Some fill when slots > 0 && fill > 0.0 ->
                Ok(
                    pos,
                    {
                        Id = id
                        Name = name
                        Pos = { X = 0.0; Y = 0.0 }
                        FillSlots = slots
                        FillTimeS = fill
                    }
                )
            | _ ->
                Error(
                    sprintf
                        "row=%d missing or invalid water source fields (source_id, name, latitude/longitude or x_m/y_m, fill_slots, fill_time_s)"
                        rowNum
                ))

    /// Fire fields default so that a target list for a touch demand needs
    /// only an id, a name and a position.
    let readSectors (path: string) =
        readRows path (fun rowNum row ->
            let numbers =
                allOk
                    [
                        floatOr "area_ha" 1.0 (fun x -> x > 0.0) row
                        floatOr "initial_intensity" 0.0 (fun x -> x >= 0.0 && x <= 1.0) row
                        floatOr "asset_priority" 1.0 (fun x -> x >= 0.0) row
                        intOr "drop_slots" 1 (fun n -> n > 0) row |> Result.map float
                        intOr "touches" 1 (fun n -> n > 0) row |> Result.map float
                    ]

            match tryGet "sector_id" row, tryGet "name" row, rawPos row, numbers with
            | Some id, Some name, Some pos, Ok [ area; intensity; priority; drops; touches ] ->
                let neighbors =
                    tryGet "neighbors" row
                    |> Option.map (fun s ->
                        s.Split(';', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                        |> List.ofArray)
                    |> Option.defaultValue []

                Ok(
                    pos,
                    {
                        Id = id
                        Name = name
                        Pos = { X = 0.0; Y = 0.0 }
                        AreaHa = area
                        InitialIntensity = intensity
                        AssetPriority = priority
                        DropSlots = int drops
                        Touches = int touches
                        Neighbors = neighbors
                    }
                )
            | _, _, _, Error column -> Error(sprintf "row=%d invalid sector field '%s'" rowNum column)
            | _ -> Error(sprintf "row=%d missing sector_id, name or position (latitude/longitude or x_m/y_m)" rowNum))

    /// The lane geometry and separation minima are optional columns with the
    /// outdoor defaults; an indoor fleet overrides them.
    let readFleet (path: string) =
        readRows path (fun rowNum row ->
            let optional =
                allOk
                    [
                        floatOr "payload_l" 0.0 (fun x -> x >= 0.0) row
                        floatOr "swap_time_s" (Scheduling.batterySwapTimeMin * 60.0) (fun x -> x >= 0.0) row
                        floatOr "lane_base_alt_m" 40.0 (fun x -> x > 0.0) row
                        floatOr "lane_layer_step_m" 20.0 (fun x -> x > 0.0) row
                        floatOr "lane_return_offset_m" 10.0 (fun x -> x > 0.0) row
                        floatOr "min_separation_m" Safety.formationFollowingDistanceMeters (fun x -> x > 0.0) row
                        floatOr "min_vertical_m" Safety.minSwarmSeparationMeters (fun x -> x > 0.0) row
                    ]

            match
                tryGet "model" row,
                tryInt (tryGet "count" row),
                tryFloat (tryGet "loaded_speed_ms" row),
                tryFloat (tryGet "empty_speed_ms" row),
                tryFloat (tryGet "endurance_min" row),
                tryFloat (tryGet "drop_time_s" row),
                tryFloat (tryGet "lane_spacing_m" row),
                optional
            with
            | Some model,
              Some count,
              Some loaded,
              Some empty,
              Some endurance,
              Some drop,
              Some spacing,
              Ok [ payload; swap; baseAlt; step; returnOffset; minSep; minVert ] when
                count > 0 && loaded > 0.0 && empty > 0.0 && endurance > 0.0 && drop >= 0.0 && spacing > 0.0
                ->
                Ok
                    {
                        Model = model
                        Count = count
                        PayloadL = payload
                        LoadedSpeedMs = loaded
                        EmptySpeedMs = empty
                        EnduranceMin = endurance
                        DropTimeS = drop
                        SwapTimeS = swap
                        LaneSpacingM = spacing
                        LaneBaseAltM = baseAlt
                        LaneStepM = step
                        ReturnOffsetM = returnOffset
                        MinSeparationM = minSep
                        MinVerticalM = minVert
                    }
            | _, _, _, _, _, _, _, Error column -> Error(sprintf "row=%d invalid fleet field '%s'" rowNum column)
            | _ -> Error(sprintf "row=%d missing or invalid fleet fields" rowNum))

    /// Event times are in ticks: column `tick`, or `minute` for the fire files
    /// (they are the same thing at a one-minute tick).
    let readEvents (path: string) =
        readRows path (fun rowNum row ->
            let target = tryGet "target" row
            let value = tryFloat (tryGet "value" row)

            // Values are range-checked here; whether a target id exists is
            // checked once the sources and targets are loaded.
            let event =
                match tryGet "event" row |> Option.map (fun s -> s.ToLowerInvariant()), target, value with
                | Some "wind_dir", _, Some deg -> Some(WindDirection deg)
                | Some "wind_speed", _, Some ms when ms >= 0.0 -> Some(WindSpeed ms)
                | Some "close_source", Some id, _ -> Some(CloseSource id)
                | Some "open_source", Some id, _ -> Some(OpenSource id)
                | Some "spot_fire", Some id, Some v
                | Some "retouch", Some id, Some v when v > 0.0 -> Some(SpotFire(id, v))
                | Some "lose_drones", _, Some n when n >= 1.0 && n = Math.Floor n -> Some(LoseDrones(int n))
                | _ -> None

            let at = tryInt (tryGet "tick" row) |> Option.orElse (tryInt (tryGet "minute" row))

            match at, event with
            | Some t, Some e when t >= 0 -> Ok { At = t; Event = e }
            | _ ->
                Error(
                    sprintf
                        "row=%d missing or invalid event fields (tick or minute >= 0; wind_dir, wind_speed >= 0, close_source, open_source, spot_fire or retouch with value > 0, lose_drones with a whole value >= 1)"
                        rowNum
                ))

    /// The events' time column, so a caller can warn when a fire file's
    /// `minute` column is read at a one-second tick.
    let eventTimeColumn (path: string) =
        let rows, _ = Data.readCsvWithHeaderWithErrors path

        rows
        |> List.tryHead
        |> Option.bind (fun row ->
            if row.Values.ContainsKey "tick" then Some "tick"
            elif row.Values.ContainsKey "minute" then Some "minute"
            else None)

    /// Decide the frame from every raw position and return the placement
    /// onto the local km plane, plus the geodetic origin of that plane when
    /// the input was geodetic. Mixing the two kinds of position is an error.
    let frameOf (points: RawPos list) : Result<Frame * (RawPos -> Pos) * (float * float) option, string> =
        let latLons =
            points
            |> List.choose (function
                | LatLon(lat, lon) -> Some(lat, lon)
                | Metres _ -> None)

        let metres =
            points
            |> List.choose (function
                | Metres(x, y) -> Some(x, y)
                | LatLon _ -> None)

        match latLons, metres with
        | [], [] -> Error "no positions"
        | _ :: _, _ :: _ -> Error "positions mix latitude/longitude with x_m/y_m; use one frame for all files"
        | _, [] ->
            let lat0 = latLons |> List.averageBy fst
            let lon0 = latLons |> List.averageBy snd
            let kmPerDegLat = 110.574
            let kmPerDegLon = 111.320 * Math.Cos(lat0 * Math.PI / 180.0)

            Ok(
                Geodetic,
                (function
                | LatLon(lat, lon) ->
                    {
                        X = (lon - lon0) * kmPerDegLon
                        Y = (lat - lat0) * kmPerDegLat
                    }
                | Metres(x, y) -> { X = x / 1000.0; Y = y / 1000.0 }),
                Some(lat0, lon0)
            )
        | [], _ ->
            Ok(
                LocalMetres,
                (function
                | Metres(x, y) -> { X = x / 1000.0; Y = y / 1000.0 }
                | LatLon _ -> { X = 0.0; Y = 0.0 }),
                None
            )
