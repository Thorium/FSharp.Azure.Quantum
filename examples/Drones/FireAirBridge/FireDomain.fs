/// Forest-fire air bridge: domain types, local geometry and CSV parsing.
///
/// Positions are read as latitude/longitude and projected onto a local flat
/// plane in kilometres (x = east, y = north). The scenario spans a few km, so
/// an equirectangular projection is accurate to well under a metre here.
module FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.FireDomain

open System
open System.Globalization

open FSharp.Azure.Quantum.Examples.Common

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

    let private pointSegmentKm (p: Pos) (a: Pos) (b: Pos) =
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

/// A lake, river or reservoir where drones fill up. Its fill slots are the
/// bottleneck most often: a lake with 4 slots and a 25 s fill passes at most
/// 4 * 60 / 25 = 9.6 drones per minute, however many drones are sent there.
type WaterSource =
    {
        Id: string
        Name: string
        Pos: Pos
        FillSlots: int
        FillTimeS: float
    }

/// A patch of the fire front (or unburnt forest next to it).
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
        Neighbors: string list
    }

/// One class of firefighting drone. The fleet is homogeneous in this example.
type DroneClass =
    {
        Model: string
        Count: int
        PayloadL: float
        LoadedSpeedMs: float
        EmptySpeedMs: float
        EnduranceMin: float
        DropTimeS: float
        /// In-trail spacing on a lane; sets the lane's headway capacity.
        LaneSpacingM: float
    }

type ScenarioEvent =
    /// Direction the wind blows TOWARD, compass degrees.
    | WindDirection of toDeg: float
    | WindSpeed of ms: float
    | CloseSource of sourceId: string
    | OpenSource of sourceId: string
    | SpotFire of sectorId: string * intensity: float
    | LoseDrones of count: int

type TimedEvent = { Minute: int; Event: ScenarioEvent }

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

    /// Sources and sectors carry raw (lat, lon) until the scenario origin is known.
    let readSources (path: string) =
        readRows path (fun rowNum row ->
            match
                tryGet "source_id" row,
                tryGet "name" row,
                tryFloat (tryGet "latitude" row),
                tryFloat (tryGet "longitude" row),
                tryInt (tryGet "fill_slots" row),
                tryFloat (tryGet "fill_time_s" row)
            with
            | Some id, Some name, Some lat, Some lon, Some slots, Some fill when slots > 0 && fill > 0.0 ->
                Ok(
                    (lat, lon),
                    {
                        Id = id
                        Name = name
                        Pos = { X = 0.0; Y = 0.0 }
                        FillSlots = slots
                        FillTimeS = fill
                    }
                )
            | _ -> Error(sprintf "row=%d missing or invalid water source fields" rowNum))

    let readSectors (path: string) =
        readRows path (fun rowNum row ->
            match
                tryGet "sector_id" row,
                tryGet "name" row,
                tryFloat (tryGet "latitude" row),
                tryFloat (tryGet "longitude" row),
                tryFloat (tryGet "area_ha" row),
                tryFloat (tryGet "initial_intensity" row),
                tryFloat (tryGet "asset_priority" row),
                tryInt (tryGet "drop_slots" row)
            with
            | Some id, Some name, Some lat, Some lon, Some area, Some intensity, Some priority, Some drops when
                area > 0.0 && drops > 0
                ->
                let neighbors =
                    tryGet "neighbors" row
                    |> Option.map (fun s ->
                        s.Split(';', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
                        |> List.ofArray)
                    |> Option.defaultValue []

                Ok(
                    (lat, lon),
                    {
                        Id = id
                        Name = name
                        Pos = { X = 0.0; Y = 0.0 }
                        AreaHa = area
                        InitialIntensity = Math.Clamp(intensity, 0.0, 1.0)
                        AssetPriority = priority
                        DropSlots = drops
                        Neighbors = neighbors
                    }
                )
            | _ -> Error(sprintf "row=%d missing or invalid fire sector fields" rowNum))

    let readFleet (path: string) =
        readRows path (fun rowNum row ->
            match
                tryGet "model" row,
                tryInt (tryGet "count" row),
                tryFloat (tryGet "payload_l" row),
                tryFloat (tryGet "loaded_speed_ms" row),
                tryFloat (tryGet "empty_speed_ms" row),
                tryFloat (tryGet "endurance_min" row),
                tryFloat (tryGet "drop_time_s" row),
                tryFloat (tryGet "lane_spacing_m" row)
            with
            | Some model, Some count, Some payload, Some loaded, Some empty, Some endurance, Some drop, Some spacing when
                count > 0 && payload > 0.0 && loaded > 0.0 && empty > 0.0 && spacing > 0.0
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
                        LaneSpacingM = spacing
                    }
            | _ -> Error(sprintf "row=%d missing or invalid fleet fields" rowNum))

    let readEvents (path: string) =
        readRows path (fun rowNum row ->
            let target = tryGet "target" row
            let value = tryFloat (tryGet "value" row)

            let event =
                match tryGet "event" row |> Option.map (fun s -> s.ToLowerInvariant()), target, value with
                | Some "wind_dir", _, Some deg -> Some(WindDirection deg)
                | Some "wind_speed", _, Some ms -> Some(WindSpeed ms)
                | Some "close_source", Some id, _ -> Some(CloseSource id)
                | Some "open_source", Some id, _ -> Some(OpenSource id)
                | Some "spot_fire", Some id, Some i -> Some(SpotFire(id, i))
                | Some "lose_drones", _, Some n -> Some(LoseDrones(int n))
                | _ -> None

            match tryInt (tryGet "minute" row), event with
            | Some minute, Some e -> Ok { Minute = minute; Event = e }
            | _ -> Error(sprintf "row=%d missing or invalid event fields" rowNum))

    /// Project raw lat/lon onto local km around the centroid of all points.
    let project (points: ((float * float) * 'T) list) (setPos: Pos -> 'T -> 'T) (origin: float * float) : 'T list =
        let lat0, lon0 = origin
        let kmPerDegLat = 110.574
        let kmPerDegLon = 111.320 * Math.Cos(lat0 * Math.PI / 180.0)

        points
        |> List.map (fun ((lat, lon), item) ->
            setPos
                {
                    X = (lon - lon0) * kmPerDegLon
                    Y = (lat - lat0) * kmPerDegLat
                }
                item)
