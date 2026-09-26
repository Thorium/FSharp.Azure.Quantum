/// From flow to flights: whole drones on the lanes, and the missions they fly.
///
/// The planner's answer is a rate: "1.3 drones circulate on PAD>C4 this tick".
/// Nobody can fly 1.3 drones. This module turns that flow into SORTIES, one
/// aircraft flying one corridor for a whole number of cycles, second by
/// second, so that the in-trail spacing the evidence relies on is a property
/// of the launch times:
///
/// - Each aircraft has its own pad on an arc around its home source, facing
///   away from the source's targets. It fills (or turns around) above its own
///   pad, so every leg is either vertical at the pad, radial to the source
///   centre, or a lane. Radial legs meet only at the centre.
/// - Drop slots sit on a ring at the target, one lane spacing apart and at
///   least one spacing from the centre, approached radially from the centre.
/// - Before a sortie launches, every passage of every cycle (source centre out
///   and back, target centre in and out), every fill (a source fills at most
///   `fill_slots` aircraft at once) and every drop-slot occupancy is booked
///   against what is already booked, a margin over the headway apart. If a
///   cycle cannot be booked the sortie is cut before it; if the first cannot,
///   the launch waits a second.
/// - An aircraft's battery is a budget of flown seconds; when the next cycle
///   does not fit, the swap happens on the pad.
///
/// The dispatcher runs INSIDE the simulation: a target is served when a whole
/// drop lands on it, not when the flow's fraction of a drop accumulates. The
/// flow model plans; the sorties are what happens.
///
/// Every sortie becomes a time-stamped 3D track, so the shared evidence code
/// computes the exact closest approach of all aircraft over the whole run, and
/// one ArduPilot mission: take off, then per cycle fill above the pad, lane
/// out, drop slot, lane back at the return altitude, above the pad, repeated
/// with DO_JUMP, then land. What the aircraft carry is what was checked.
///
/// What this does NOT model: aircraft moving between sources. An aircraft is
/// homed at one source for the run, so a plan that moves the fleet to another
/// lake shows up as a shortfall, which the follow-through check reports.
module FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.Dispatch

open System
open System.Collections.Generic
open System.IO

open FSharp.Azure.Quantum.Examples.Common
open FSharp.Azure.Quantum.Examples.Drones.Domain
open FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.FireDomain
open FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.AirBridge
open FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.Demand

module Ev = FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence
module Mav = FSharp.Azure.Quantum.Examples.Drones.MavlinkMission

// =============================================================================
// INPUT
// =============================================================================

/// One lane as the fast loop wants it flown this tick.
type LaneAt =
    {
        Corridor: Corridor
        OutAltM: float
        RetAltM: float
        /// Drones the flow model assigned (fractional).
        Drones: float
    }

type Options =
    {
        Tick: Tick
        Ticks: int
        Demand: Demand
        /// Reserved passages are this many headways apart (1 = exactly the lane
        /// spacing; more covers the autopilot's acceleration and hold rounding,
        /// which the constant-speed tracks do not carry).
        Margin: float
        /// Height under which an aircraft counts as parked in the separation check.
        GroundZ: float
    }

// =============================================================================
// GEOMETRY
// =============================================================================

type Pt = Ev.P3

let private pt x y z : Pt = { X = x; Y = y; Z = z }

let private metres (p: Pos) = (p.X * 1000.0, p.Y * 1000.0)

let private horizontal (a: Pt) (b: Pt) =
    Math.Sqrt((a.X - b.X) ** 2.0 + (a.Y - b.Y) ** 2.0)

let private onRing (centre: Pt) (radius: float) (angle: float) (z: float) =
    pt (centre.X + radius * Math.Cos angle) (centre.Y + radius * Math.Sin angle) z

/// Neighbouring pads sit this much further apart than the minimum, so that an
/// aircraft climbing off its pad beside a parked one is clear by more than a
/// rounding error.
let private padMargin = 1.02

// =============================================================================
// SORTIES
// =============================================================================

/// One aircraft flying one corridor for `Cycles` whole cycles.
type Sortie =
    {
        Drone: int
        Corridor: Corridor
        LaunchS: float
        LandS: float
        Cycles: int
        OutAltM: float
        RetAltM: float
        Pad: Pt
        DropSlot: Pt
        /// Where the aircraft is, when (a straight line between samples).
        Samples: (float * Pt)[]
        /// Seconds at which each drop lands.
        DropsAt: float list
    }

/// When one cycle occupies the shared places, relative to the sortie start.
type private CycleTimes =
    {
        FillIn: float
        FillOut: float
        OutPass: float
        TgtIn: float
        DropIn: float
        DropOut: float
        /// When the aircraft has climbed clear of the drop slot.
        DropClear: float
        TgtOut: float
        RetPass: float
        End: float
    }

type private Geom =
    {
        Pad: Pt
        SrcC: Pt
        TgtC: Pt
        Drop: Pt
        OutAlt: float
        RetAlt: float
    }

/// The path and timing of `cycles` cycles from a launch at time 0: samples and
/// the per-cycle occupancy times.
let private flyCycles (fleet: DroneClass) (c: Corridor) (g: Geom) (cycles: int) =
    let vUp = Terminal.climbMs fleet
    let vDown = Terminal.descentMs fleet
    let vLoaded = fleet.LoadedSpeedMs
    let vEmpty = fleet.EmptySpeedMs
    let laneM = c.DistanceKm * 1000.0
    let outGs = laneM / c.OutboundS
    let retGs = laneM / c.ReturnS
    let samples = ResizeArray<float * Pt>()
    let times = ResizeArray<CycleTimes>()
    let mutable t = 0.0
    let mutable here = pt g.Pad.X g.Pad.Y 0.0
    samples.Add((0.0, here))

    let leg (target: Pt) (vh: float) =
        let dh = horizontal here target
        let dz = target.Z - here.Z
        let vz = if dz > 0.0 then vUp else vDown

        let dt =
            max (if vh > 0.0 then dh / vh else 0.0) (if abs dz > 1e-9 then abs dz / vz else 0.0)

        t <- t + dt
        here <- target
        samples.Add((t, here))

    let hold (seconds: float) =
        if seconds > 0.0 then
            t <- t + seconds
            samples.Add((t, here))

    let at (p: Pt) (z: float) = pt p.X p.Y z

    for _ in 1..cycles do
        leg (at g.Pad g.OutAlt) vLoaded
        let fillIn = t
        hold c.Source.FillTimeS
        let fillOut = t
        leg (at g.SrcC g.OutAlt) vLoaded
        let outPass = t
        leg (at g.TgtC g.OutAlt) outGs
        let tgtIn = t
        leg (at g.Drop g.OutAlt) vLoaded
        let dropIn = t
        hold fleet.DropTimeS
        let dropOut = t
        leg (at g.Drop g.RetAlt) vEmpty
        let dropClear = t
        leg (at g.TgtC g.RetAlt) vEmpty
        let tgtOut = t
        leg (at g.SrcC g.RetAlt) retGs
        let retPass = t
        leg (at g.Pad g.RetAlt) vEmpty

        times.Add
            {
                FillIn = fillIn
                FillOut = fillOut
                OutPass = outPass
                TgtIn = tgtIn
                DropIn = dropIn
                DropOut = dropOut
                DropClear = dropClear
                TgtOut = tgtOut
                RetPass = retPass
                End = t
            }

    // Landing: the descent speed down to LAND_ALT_LOW, then LAND_SPEED.
    if here.Z > Terminal.landAltLowM then
        leg (at g.Pad Terminal.landAltLowM) vDown

    t <- t + here.Z / Terminal.landSpeedMs
    here <- at g.Pad 0.0
    samples.Add((t, here))
    (samples.ToArray(), times |> List.ofSeq, t)

// =============================================================================
// RESERVATIONS
// =============================================================================

/// One aircraft passing a shared centre (a source's or a target's): when, on
/// which corridor, the ray it comes in on and the ray it leaves on (unit
/// vectors out of the centre), how fast on each, and how long the lane is.
type private Passage =
    {
        T: float
        Corridor: string
        In: float * float
        Out: float * float
        GsIn: float
        GsOut: float
        LaneM: float
    }

/// What is already booked at the shared places. Two passages of one centre
/// must be far enough apart in time for three things (see `gap`):
/// - while both approach, and while both depart, the later one must not
///   close to within a spacing of the earlier, which a faster follower does
///   along the stretch where the two rays are still one spacing apart;
/// - in between, the earlier leaves along its out-ray while the later comes
///   in along its in-ray; at angle phi between those rays the two are at
///   best v dt sin(phi / 2) apart, and on one and the same ray, head on.
/// Slots are intervals with a capacity.
type private Book(spacing: float, margin: float) =
    let passages = Dictionary<string, ResizeArray<Passage>>()
    let slots = Dictionary<string, ResizeArray<float * float>>()

    let angle ((ax, ay): float * float) ((bx, by): float * float) =
        Math.Acos(Math.Clamp(ax * bx + ay * by, -1.0, 1.0))

    /// Seconds the later passage must follow the earlier by.
    let gap (earlier: Passage) (later: Passage) =
        // Along two rays at angle theta the aircraft are within a spacing of
        // each other out to the merge radius; a faster follower closes in
        // over that stretch.
        let sameWay (theta: float) (vEarlier: float) (vLater: float) =
            let merged =
                if theta < 1e-6 then
                    later.LaneM
                else
                    min later.LaneM (spacing / (2.0 * Math.Sin(theta / 2.0)))

            (spacing + max 0.0 (vLater - vEarlier) * merged / vLater) / vEarlier

        let approach = sameWay (angle earlier.In later.In) earlier.GsIn later.GsIn
        let depart = sameWay (angle earlier.Out later.Out) earlier.GsOut later.GsOut

        let crossing =
            let phi = angle earlier.Out later.In

            if phi < 1e-6 then
                Double.PositiveInfinity
            else
                spacing / (min earlier.GsOut later.GsIn * Math.Sin(phi / 2.0))

        margin * (max approach (max depart crossing))

    member _.PassageFree(key: string, p: Passage) =
        match passages.TryGetValue key with
        | true, ps ->
            ps
            |> Seq.forall (fun q ->
                if q.T <= p.T then
                    p.T - q.T >= gap q p - 1e-9
                else
                    q.T - p.T >= gap p q - 1e-9)
        | _ -> true

    /// Fewer than `capacity` booked intervals overlap [a, b].
    member _.SlotFree(key: string, a: float, b: float, capacity: int) =
        match slots.TryGetValue key with
        | true, xs -> (xs |> Seq.filter (fun (x, y) -> not (b <= x + 1e-9 || a >= y - 1e-9)) |> Seq.length) < capacity
        | _ -> true

    member _.Passage(key: string, p: Passage) =
        match passages.TryGetValue key with
        | true, ps -> ps.Add p
        | _ -> passages.[key] <- ResizeArray [ p ]

    member _.Slot(key: string, a: float, b: float) =
        match slots.TryGetValue key with
        | true, xs -> xs.Add((a, b))
        | _ -> slots.[key] <- ResizeArray [ (a, b) ]

// =============================================================================
// THE DISPATCHER
// =============================================================================

type Result =
    {
        Sorties: Sortie list
        /// Every aircraft's track over the run, parked spells included.
        Tracks: Ev.Track list
        DroneNames: string[]
        /// Source each aircraft is homed at.
        Homes: int[]
        Pads: Pt[]
        /// Whole drops that landed on each target.
        DropsPerTarget: int[]
        /// Corridor-seconds the plan asked for more aircraft than could be launched.
        ShortfallS: int
        /// The worst shortfall: (second, corridor, wanted, had).
        WorstShortfall: (int * string * int * int) option
        /// Corridors the plan opened whose whole cycle exceeds a fresh battery.
        Unflyable: string list
        /// Aircraft-seconds the plan asked for in all, so the shortfall has a scale.
        PlannedS: int
    }

/// Largest-remainder rounding of the flow's fractional drones per corridor,
/// with the total fixed at the rounded total.
let private roundAllocation (lanes: LaneAt list) =
    let total = lanes |> List.sumBy (fun l -> l.Drones) |> Math.Round |> int
    let floors = lanes |> List.map (fun l -> (l, Math.Floor l.Drones |> int))
    let left = total - (floors |> List.sumBy snd)

    floors
    |> List.sortByDescending (fun (l, f) -> l.Drones - float f)
    |> List.mapi (fun i (l, f) -> (l.Corridor.Id, if i < left then f + 1 else f))
    |> Map.ofList

/// Steps with the simulation, one tick at a time. `homing` says how many
/// aircraft each source gets (see `home`).
type Dispatcher(fleet: DroneClass, sources: WaterSource[], sectors: FireSector[], opts: Options, homing: int[]) =
    let tickS = Tick.seconds opts.Tick
    let runEndS = float opts.Ticks * tickS
    let spacing = fleet.LaneSpacingM
    let unitsPerDrop = Demand.unitsPerDrop opts.Demand fleet
    let usableS = fleet.EnduranceMin * 60.0 * (1.0 - Battery.reserveBatteryPercent / 100.0)

    let srcCentre = sources |> Array.map (fun s -> let x, y = metres s.Pos in pt x y 0.0)
    let tgtCentre = sectors |> Array.map (fun s -> let x, y = metres s.Pos in pt x y 0.0)

    let homes =
        [|
            for si in 0 .. sources.Length - 1 do
                for _ in 1 .. homing.[si] do
                    si
        |]

    // The pads face away from the source's targets: the mean direction of
    // every target from the source, plus pi.
    let awayAngle (si: int) =
        let dx = sectors |> Array.averageBy (fun s -> tgtCentre.[Array.findIndex ((=) s) sectors].X - srcCentre.[si].X)
        let dy = sectors |> Array.averageBy (fun s -> tgtCentre.[Array.findIndex ((=) s) sectors].Y - srcCentre.[si].Y)
        Math.Atan2(dy, dx) + Math.PI

    let pads =
        let counters = Array.zeroCreate sources.Length

        homes
        |> Array.map (fun si ->
            let radius, step = Terminal.padRing homing.[si] (padMargin * fleet.MinSeparationM) spacing
            let k = counters.[si]
            counters.[si] <- k + 1
            let angle = awayAngle si - Math.PI / 2.0 + float k * step
            onRing srcCentre.[si] radius angle 0.0)

    let droneNames = homes |> Array.mapi (fun d si -> sprintf "%s-%d" sources.[si].Id (d + 1))

    /// Drop slot k of target fi for a lane from source si: slot 0 straight on
    /// from the lane, the rest fanned to both sides, none in the approach.
    let dropSlot (si: int) (fi: int) (k: int) =
        let n = sectors.[fi].DropSlots
        let radius, step = Terminal.dropRing n spacing
        let s, t = srcCentre.[si], tgtCentre.[fi]
        let onward = Math.Atan2(t.Y - s.Y, t.X - s.X)
        onRing t radius (onward + (float k - float (n - 1) / 2.0) * step) 0.0

    // Per-aircraft state
    let busyUntil = Array.create homes.Length 0.0
    let flownS = Array.zeroCreate<float> homes.Length
    let lost = Array.create homes.Length false
    let sorties = ResizeArray<Sortie>()
    let book = Book(spacing, opts.Margin)
    let drops = Array.zeroCreate<int> sectors.Length
    let mutable shortfallS = 0
    let mutable plannedS = 0
    let mutable worst: (int * string * int * int) option = None
    let mutable previousFleet = homes.Length
    /// Corridors whose one whole cycle does not fit a fresh battery.
    let unflyable = HashSet<string>()

    /// Try to book a sortie of up to `maxCycles` cycles launching at `t0`.
    /// Returns the cycles that could be booked (0 = not even the first).
    let tryBook (t0: float) (c: Corridor) (g: Geom) (maxCycles: int) (commit: bool) =
        let laneM = c.DistanceKm * 1000.0
        let outGs = laneM / c.OutboundS
        let retGs = laneM / c.ReturnS
        let gapOut = opts.Margin * spacing / outGs
        let _, times, _ = flyCycles fleet c g maxCycles
        let src = string c.SourceIdx
        let tgt = string c.SectorIdx
        let dropKey = sprintf "drop:%d:%.3f:%.3f" c.SectorIdx g.Drop.X g.Drop.Y

        // Rays out of each centre: how the aircraft comes in and leaves.
        let ray (from: Pt) (toward: Pt) =
            let d = horizontal from toward
            if d < 1e-9 then (1.0, 0.0) else ((toward.X - from.X) / d, (toward.Y - from.Y) / d)

        let padRay = ray g.SrcC g.Pad
        let laneRay = ray g.SrcC g.TgtC
        let backRay = ray g.TgtC g.SrcC
        let slotRay = ray g.TgtC g.Drop

        let passage (t: float) (inRay, gsIn) (outRay, gsOut) =
            {
                T = t
                Corridor = c.Id
                In = inRay
                Out = outRay
                GsIn = gsIn
                GsOut = gsOut
                LaneM = laneM
            }

        let passages (ct: CycleTimes) =
            [
                ("src-out:" + src, passage (t0 + ct.OutPass) (padRay, fleet.LoadedSpeedMs) (laneRay, outGs))
                ("src-ret:" + src, passage (t0 + ct.RetPass) (laneRay, retGs) (padRay, fleet.EmptySpeedMs))
                ("tgt-in:" + tgt, passage (t0 + ct.TgtIn) (backRay, outGs) (slotRay, fleet.LoadedSpeedMs))
                ("tgt-out:" + tgt, passage (t0 + ct.TgtOut) (slotRay, fleet.EmptySpeedMs) (backRay, retGs))
            ]

        let cycleFree (ct: CycleTimes) =
            (passages ct |> List.forall (fun (key, p) -> book.PassageFree(key, p)))
            && book.SlotFree("fill:" + src, t0 + ct.FillIn, t0 + ct.FillOut, c.Source.FillSlots)
            && book.SlotFree(dropKey, t0 + ct.DropIn, t0 + ct.DropClear + gapOut, 1)

        let bookable = times |> List.takeWhile cycleFree |> List.length

        if commit then
            for ct in times |> List.truncate bookable do
                for key, p in passages ct do
                    book.Passage(key, p)

                book.Slot("fill:" + src, t0 + ct.FillIn, t0 + ct.FillOut)
                book.Slot(dropKey, t0 + ct.DropIn, t0 + ct.DropClear + gapOut)

        bookable

    /// Launch aircraft d on the lane at t0 for up to `wantedCycles` cycles.
    let launch (d: int) (t0: float) (lane: LaneAt) (wantedCycles: int) =
        let c = lane.Corridor
        let si, fi = c.SourceIdx, c.SectorIdx

        let geom (dk: int) =
            {
                Pad = pads.[d]
                SrcC = srcCentre.[si]
                TgtC = tgtCentre.[fi]
                Drop = dropSlot si fi dk
                OutAlt = lane.OutAltM
                RetAlt = lane.RetAltM
            }

        // One cycle, and the final descent onto the pad that every sortie ends
        // with: the battery must cover both.
        let cycleS, descentS =
            let _, times, endS = flyCycles fleet c (geom 0) 1
            (times.Head.End, endS - times.Head.End)

        let byBattery = int (Math.Floor((usableS - flownS.[d] - descentS) / cycleS))
        let byRun = max 1 (int (Math.Ceiling((runEndS - t0) / cycleS)))

        if byBattery < 1 && flownS.[d] <= 0.0 then
            // Not even a fresh battery fits one cycle: nothing to wait for.
            unflyable.Add c.Id |> ignore
            false
        elif byBattery < 1 then
            // Swap on the pad, then try again.
            busyUntil.[d] <- t0 + fleet.SwapTimeS
            flownS.[d] <- 0.0
            false
        else
            let maxCycles = max 1 (min wantedCycles (min byBattery byRun))

            let best =
                [
                    for dk in 0 .. sectors.[fi].DropSlots - 1 do
                        let g = geom dk
                        (g, tryBook t0 c g maxCycles false)
                ]
                |> List.sortByDescending snd
                |> List.tryHead

            match best with
            | Some(g, n) when n >= 1 ->
                tryBook t0 c g n true |> ignore
                let samples, times, endS = flyCycles fleet c g n

                sorties.Add
                    {
                        Drone = d
                        Corridor = c
                        LaunchS = t0
                        LandS = t0 + endS
                        Cycles = n
                        OutAltM = lane.OutAltM
                        RetAltM = lane.RetAltM
                        Pad = pads.[d]
                        DropSlot = g.Drop
                        Samples = samples |> Array.map (fun (t, p) -> (t0 + t, p))
                        DropsAt = times |> List.map (fun ct -> t0 + ct.DropOut)
                    }

                // On the ground until it has disarmed and the launcher has put
                // the next sortie on board.
                busyUntil.[d] <- t0 + endS + Terminal.turnaroundS
                flownS.[d] <- flownS.[d] + endS
                true
            | _ -> false

    /// One tick: launch what the lanes want, second by second, and return the
    /// units that whole drops landed on each target during this tick.
    member _.Step(tick: int, fleetSize: int, lanes: LaneAt list, remainingUnits: float[]) : float[] =
        let from = float tick * tickS
        let until = float (tick + 1) * tickS

        // Aircraft lost this tick: parked ones first, then whoever would land
        // soonest. A lost aircraft's sortie ends where it is: its track stops,
        // its later drops never land. Its bookings stay, which only keeps
        // others away from slots that are now free.
        if fleetSize < previousFleet then
            let toLose = previousFleet - fleetSize

            let losing =
                Array.init homes.Length id
                |> Array.filter (fun d -> not lost.[d])
                |> Array.sortBy (fun d -> ((if busyUntil.[d] <= from then 0 else 1), busyUntil.[d]))
                |> Array.truncate toLose

            for d in losing do
                lost.[d] <- true

                for i in 0 .. sorties.Count - 1 do
                    let s = sorties.[i]

                    if s.Drone = d && s.LandS > from && s.LaunchS < from then
                        let track: Ev.Track = { AircraftId = ""; Samples = s.Samples }

                        let cut =
                            match Ev.positionAt track from with
                            | Some p -> [| (from, p) |]
                            | None -> [||]

                        sorties.[i] <-
                            { s with
                                LandS = from
                                Samples = Array.append (s.Samples |> Array.filter (fun (t, _) -> t < from)) cut
                                DropsAt = s.DropsAt |> List.filter (fun at -> at < from)
                            }

                busyUntil.[d] <- Double.PositiveInfinity

            previousFleet <- fleetSize

        let wanted = lanes |> List.filter (fun l -> l.Drones > 1e-9)
        let targets = roundAllocation wanted

        for second in int from .. int until - 1 do
            let t0 = float second

            let assigned (cid: string) =
                sorties |> Seq.filter (fun s -> s.Corridor.Id = cid && s.LandS > t0) |> Seq.length

            // One launch per source per second at most (the headway rule
            // would refuse a second one anyway).
            let launchedFrom = HashSet<int>()

            for lane in wanted |> List.sortByDescending (fun l -> l.Drones) do
                let cid = lane.Corridor.Id
                let want = targets |> Map.tryFind cid |> Option.defaultValue 0
                let have = assigned cid
                plannedS <- plannedS + want

                if want > have then
                    let si = lane.Corridor.SourceIdx
                    let fi = lane.Corridor.SectorIdx

                    // Enough cycles to finish the target with the aircraft the
                    // plan puts on it; a fire wants water for as long as the
                    // battery lasts.
                    let cycles =
                        if Double.IsPositiveInfinity remainingUnits.[fi] then
                            Int32.MaxValue
                        else
                            max 1 (int (Math.Ceiling(remainingUnits.[fi] / unitsPerDrop / float (max 1 want))))

                    let free =
                        Array.init homes.Length id
                        |> Array.filter (fun d -> homes.[d] = si && not lost.[d] && busyUntil.[d] <= t0)
                        |> Array.sortBy (fun d -> flownS.[d])

                    let mutable launched = false

                    if not (launchedFrom.Contains si) then
                        for d in free do
                            if not launched && launch d t0 lane cycles then
                                launched <- true
                                launchedFrom.Add si |> ignore

                    let haveNow = if launched then have + 1 else have

                    if want > haveNow then
                        shortfallS <- shortfallS + (want - haveNow)

                        match worst with
                        | Some(_, _, w, h) when w - h >= want - haveNow -> ()
                        | _ -> worst <- Some(second, cid, want, haveNow)

        // Drops that land during this tick.
        let delivered = Array.zeroCreate<float> sectors.Length

        for s in sorties do
            for at in s.DropsAt do
                if at >= from && at < until then
                    delivered.[s.Corridor.SectorIdx] <- delivered.[s.Corridor.SectorIdx] + unitsPerDrop
                    drops.[s.Corridor.SectorIdx] <- drops.[s.Corridor.SectorIdx] + 1

        delivered

    member _.Finish() : Result =
        let tracks =
            [
                for d in 0 .. homes.Length - 1 do
                    let mine =
                        sorties |> Seq.filter (fun s -> s.Drone = d) |> Seq.sortBy (fun s -> s.LaunchS) |> List.ofSeq

                    let ground = pt pads.[d].X pads.[d].Y 0.0

                    let samples =
                        [|
                            yield (0.0, ground)

                            for s in mine do
                                yield! s.Samples |> Array.filter (fun (t, _) -> t > 0.0)

                            let last = mine |> List.tryLast |> Option.map (fun s -> s.LandS) |> Option.defaultValue 0.0

                            // A lost aircraft is out of the picture from its loss on;
                            // anything else is parked on its pad to the end.
                            if last < runEndS && not lost.[d] then
                                yield (runEndS, ground)
                        |]

                    { Ev.AircraftId = droneNames.[d]; Ev.Samples = samples }
            ]

        {
            Sorties = List.ofSeq sorties
            Tracks = tracks
            DroneNames = droneNames
            Homes = homes
            Pads = pads
            DropsPerTarget = drops
            ShortfallS = shortfallS
            WorstShortfall = worst
            Unflyable = unflyable |> List.ofSeq |> List.sort
            PlannedS = plannedS
        }

/// Home the fleet: each source gets aircraft in proportion to the value of
/// its corridors at the start (largest remainder), at least one wherever a
/// corridor is worth flying. An aircraft stays at its source for the run.
let home (fleet: DroneClass) (sources: WaterSource[]) (worth: Corridor -> float) (corridors: Corridor[]) : int[] =
    let perSource =
        Array.init sources.Length (fun si ->
            corridors |> Array.filter (fun c -> c.SourceIdx = si) |> Array.sumBy (fun c -> max 0.0 (worth c)))

    let total = Array.sum perSource

    if total <= 0.0 then
        Array.init sources.Length (fun si -> if si = 0 then fleet.Count else 0)
    else
        let shares = perSource |> Array.map (fun d -> float fleet.Count * d / total)
        let floors = shares |> Array.map (fun s -> int (Math.Floor s))
        let mutable left = fleet.Count - Array.sum floors

        for si in Array.init sources.Length id |> Array.sortByDescending (fun si -> shares.[si] - float floors.[si]) do
            if left > 0 then
                floors.[si] <- floors.[si] + 1
                left <- left - 1

        for si in 0 .. sources.Length - 1 do
            if perSource.[si] > 0.0 && floors.[si] = 0 then
                let donor = Array.init sources.Length id |> Array.maxBy (fun j -> floors.[j])

                if floors.[donor] > 1 then
                    floors.[donor] <- floors.[donor] - 1
                    floors.[si] <- 1

        floors

/// Most aircraft off the ground at the same second.
let peakAirborne (opts: Options) (r: Result) =
    let runEndS = int (float opts.Ticks * Tick.seconds opts.Tick)

    [ 0..runEndS ]
    |> List.map (fun s ->
        r.Tracks
        |> List.filter (fun t ->
            match Ev.positionAt t (float s) with
            | Some p -> p.Z > opts.GroundZ
            | None -> false)
        |> List.length)
    |> List.fold max 0

// =============================================================================
// EVIDENCE
// =============================================================================

/// Checks the dispatched flights add to the pack: the exact closest approach
/// of every aircraft over the run, and whether whole drops did the job.
let checks (fleet: DroneClass) (sectors: FireSector[]) (opts: Options) (events: TimedEvent list) (r: Result) : Ev.Check list =
    // Vertical distance counts by the fleet's vertical minimum: heights are
    // scaled so that one MinVerticalM reads as one MinSeparationM, and the
    // Euclidean closest approach then applies the horizontal minimum to both.
    let zScale = fleet.MinSeparationM / fleet.MinVerticalM

    let scaled =
        r.Tracks
        |> List.map (fun t ->
            { t with
                Samples = t.Samples |> Array.map (fun (time, p) -> (time, { p with Z = p.Z * zScale }))
            })

    let closest =
        Ev.closestApproach 1.0 (opts.GroundZ * zScale) scaled
        |> Option.map (fun c -> { c with Where = { c.Where with Z = c.Where.Z / zScale } })

    let separation =
        Ev.Checks.separation
            // A hair under the minimum: a lane exactly one minimum above another
            // passes on its geometry, not on floating-point luck.
            (fleet.MinSeparationM * (1.0 - 1e-9))
            (sprintf
                "exact closest approach between every pair of dispatched tracks (pads, lanes at their altitudes, drop slots, constant speed per leg), parked aircraft included; heights scaled by %.2f so that %.1f m vertical counts as %.1f m"
                zScale
                fleet.MinVerticalM
                fleet.MinSeparationM)
            closest

    // Share of the plan's aircraft-seconds that whole aircraft actually flew.
    let flownShare =
        if r.PlannedS <= 0 then
            1.0
        else
            1.0 - float r.ShortfallS / float r.PlannedS

    let shortfall =
        match r.WorstShortfall with
        | Some(s, cid, w, h) ->
            sprintf
                "; flew %.0f%% of the plan's aircraft-seconds, worst at t=%d s on %s (wanted %d, had %d)"
                (flownShare * 100.0)
                s
                cid
                w
                h
        | None -> "; flew all of the plan's aircraft-seconds"

    let unflyable =
        if r.Unflyable.IsEmpty then
            []
        else
            [
                sprintf
                    "one whole cycle exceeds a fresh battery on: %s (the plan's cycle is shorter than what flies)"
                    (String.Join(" ", r.Unflyable))
            ]

    let followThrough =
        match opts.Demand with
        | Touches _ ->
            let required =
                sectors
                |> Array.map (fun s ->
                    float s.Touches
                    + (events
                       |> List.sumBy (fun e ->
                           match e.Event with
                           | SpotFire(id, v) when id = s.Id -> v
                           | _ -> 0.0))
                    |> Math.Ceiling
                    |> int)

            let missing =
                Array.zip sectors (Array.zip required r.DropsPerTarget)
                |> Array.filter (fun (_, (need, got)) -> got < need)

            Ev.Checks.contingency
                "Whole aircraft deliver every touch owed"
                "drops landed by dispatched sorties per target vs. touches owed (initial plus retouch events)"
                (sprintf
                    "%d of %d targets touched as owed; %d drops in %d sorties%s"
                    (sectors.Length - missing.Length)
                    sectors.Length
                    (Array.sum r.DropsPerTarget)
                    r.Sorties.Length
                    shortfall)
                (if Array.isEmpty missing && r.Unflyable.IsEmpty then Ev.Pass else Ev.Fail)
                ((missing
                  |> Array.truncate 10
                  |> Array.map (fun (s, (need, got)) -> sprintf "%s: %d of %d" s.Id got need)
                  |> List.ofArray)
                 @ unflyable)
        | Fire _ ->
            // The fire's water came from these drops (the dispatcher drives the
            // simulation), so the question is whether the aircraft kept up with
            // what the plan asked for. Launch spacing and battery swaps always
            // cost some of it; the example's working threshold is 75%.
            Ev.Checks.contingency
                "Whole aircraft fly the corridors the plan opens"
                "aircraft-seconds flown on each corridor vs. the plan's rounded allocation, summed over the run; the example's working threshold is 75%"
                (sprintf "%d drops in %d sorties%s" (Array.sum r.DropsPerTarget) r.Sorties.Length shortfall)
                (if flownShare >= 0.75 && r.Unflyable.IsEmpty then Ev.Pass else Ev.Fail)
                unflyable

    [ separation; followThrough ]

// =============================================================================
// MAVLINK EXPORT
// =============================================================================

/// One ArduPilot mission per sortie, the launcher's schedule, and a CSV of the
/// sorties. `home` is the geodetic position of the local frame's origin.
let export (outDir: string) (fleet: DroneClass) (home: Mav.GeoCoordinate) (r: Result) =
    let toGeo (p: Pt) : Mav.GeoCoordinate =
        let g = Mav.localToGeo home { North = p.Y; East = p.X; Down = 0.0 }
        { g with Altitude = p.Z }

    let acceptM = min 0.5 (fleet.LaneSpacingM / 4.0)

    let parameters (retAlt: float) =
        [
            "RTL_ALT", Math.Round(retAlt * 100.0) // cm: the return lane's altitude
            "RTL_ALT_FINAL", 0.0
            "RTL_LOIT_TIME", 0.0
            "DISARM_DELAY", Terminal.disarmDelayS
            "LAND_ALT_LOW", Terminal.landAltLowM * 100.0 // cm
            "WPNAV_SPEED", Math.Round(fleet.LoadedSpeedMs * 100.0) // cm/s; legs set their own with DO_CHANGE_SPEED
            "WPNAV_SPEED_UP", Math.Round(Terminal.climbMs fleet * 100.0)
            "WPNAV_SPEED_DN", Math.Round(Terminal.descentMs fleet * 100.0)
            // Copter judges a waypoint reached by WPNAV_RADIUS (cm), not by the
            // item's own acceptance radius; the tracks turn at the waypoint.
            "WPNAV_RADIUS", Math.Round(acceptM * 100.0)
            // The last metres onto the pad go at ArduPilot's default landing
            // speed, slower than the modelled descent; the pad is the aircraft's
            // own, so the extra seconds move nothing else, and the launcher
            // starts the next sortie only once the vehicle has disarmed.
            "LAND_SPEED", Terminal.landSpeedMs * 100.0
            "FS_GCS_ENABLE", 1.0
            "FS_GCS_TIMEOUT", Safety.signalLossRthTriggerSec
            "FS_THR_ENABLE", 1.0
            // Lost link or RC in AUTO: continue the mission (bits 0, 1), and
            // continue a landing (bit 3). The modelled recovery is to finish
            // the cycle along the lanes, which is what the mission does.
            "FS_OPTIONS", 11.0
            // Low battery warns only: the sortie already fits endurance minus
            // reserve, and a lone RTL would cut across other lanes.
            "BATT_FS_LOW_ACT", 0.0
        ]

    let waypointHold (p: Pt) (holdS: float) (items: ResizeArray<Mav.MissionItem>) =
        let whole = Math.Floor holdS
        items.Add(Mav.waypoint whole acceptM (toGeo p) items.Count)

        if holdS - whole > 1e-3 then
            items.Add(Mav.navDelay (Math.Round(holdS - whole, 3)) items.Count)

    let mission (k: int) (s: Sortie) : Mav.ScheduledMission =
        let at (p: Pt) z = pt p.X p.Y z
        let c = s.Corridor
        let srcC = (let x, y = metres c.Source.Pos in pt x y 0.0)
        let tgtC = (let x, y = metres c.Sector.Pos in pt x y 0.0)
        let items = ResizeArray<Mav.MissionItem>()
        items.Add(Mav.takeoff s.OutAltM (toGeo s.Pad) items.Count)
        // The cycle loop starts here (the uploaded mission numbers home as 0).
        let loopStart = items.Count + 1
        items.Add(Mav.setSpeed fleet.LoadedSpeedMs items.Count)
        waypointHold (at s.Pad s.OutAltM) c.Source.FillTimeS items
        // A copter's DO_CHANGE_SPEED sets ground speed, so the lane legs command
        // the ground speed the tracks were checked at (still air plus wind),
        // and the terminal legs the still-air speeds.
        let laneM = c.DistanceKm * 1000.0
        let outGs = laneM / c.OutboundS
        let retGs = laneM / c.ReturnS
        items.Add(Mav.waypoint 0.0 acceptM (toGeo (at srcC s.OutAltM)) items.Count)
        items.Add(Mav.setSpeed outGs items.Count)
        items.Add(Mav.waypoint 0.0 acceptM (toGeo (at tgtC s.OutAltM)) items.Count)
        items.Add(Mav.setSpeed fleet.LoadedSpeedMs items.Count)
        waypointHold (at s.DropSlot s.OutAltM) fleet.DropTimeS items
        items.Add(Mav.setSpeed fleet.EmptySpeedMs items.Count)
        items.Add(Mav.waypoint 0.0 acceptM (toGeo (at s.DropSlot s.RetAltM)) items.Count)
        items.Add(Mav.waypoint 0.0 acceptM (toGeo (at tgtC s.RetAltM)) items.Count)
        items.Add(Mav.setSpeed retGs items.Count)
        items.Add(Mav.waypoint 0.0 acceptM (toGeo (at srcC s.RetAltM)) items.Count)
        items.Add(Mav.setSpeed fleet.EmptySpeedMs items.Count)
        items.Add(Mav.waypoint 0.0 acceptM (toGeo (at s.Pad s.RetAltM)) items.Count)

        if s.Cycles > 1 then
            items.Add(Mav.doJump loopStart (s.Cycles - 1) items.Count)

        items.Add(Mav.landAt (toGeo s.Pad) items.Count)

        {
            Mission =
                {
                    Drone =
                        {
                            SystemId = s.Drone + 1
                            ComponentId = 1
                            Name = sprintf "%s_s%d" r.DroneNames.[s.Drone] k
                            ConnectionString = sprintf "tcp:127.0.0.1:%d" (5760 + 10 * s.Drone)
                        }
                    HomePosition = { toGeo s.Pad with Altitude = home.Altitude }
                    Items = List.ofSeq items
                    // One parameter set per vehicle: RTL_ALT is the highest return
                    // lane any of its sorties flies, so the launcher sees no
                    // conflict between them.
                    Parameters =
                        parameters (
                            r.Sorties
                            |> List.filter (fun o -> o.Drone = s.Drone)
                            |> List.map (fun o -> o.RetAltM)
                            |> List.max
                        )
                }
            LaunchS = s.LaunchS
        }

    let missions =
        r.Sorties
        |> List.groupBy (fun s -> s.Drone)
        |> List.collect (fun (_, ss) -> ss |> List.sortBy (fun s -> s.LaunchS) |> List.mapi (fun k s -> mission (k + 1) s))

    let dir = Path.Combine(outDir, "mavlink")
    Directory.CreateDirectory dir |> ignore
    let plain = missions |> List.map (fun m -> m.Mission)
    Mav.QGroundControl.writeAll dir plain
    Mav.WaypointFile.writeAll dir plain
    Mav.ParamFile.writeAll dir plain
    Mav.FsxScript.writeFile (Path.Combine(dir, "mavlink_show.fsx")) missions
    missions.Length

/// The sorties as a CSV, for people and for the launcher's operator.
let writeSorties (outDir: string) (r: Result) =
    Reporting.writeCsv
        (Path.Combine(outDir, "sorties.csv"))
        [
            "drone"
            "sortie"
            "corridor"
            "launch_s"
            "land_s"
            "cycles"
            "outbound_alt_m"
            "return_alt_m"
            "drops_at_s"
        ]
        [
            for d, ss in r.Sorties |> List.groupBy (fun s -> s.Drone) |> List.sortBy fst do
                for k, s in ss |> List.sortBy (fun s -> s.LaunchS) |> List.indexed do
                    [
                        r.DroneNames.[d]
                        string (k + 1)
                        s.Corridor.Id
                        sprintf "%.1f" s.LaunchS
                        sprintf "%.1f" s.LandS
                        string s.Cycles
                        sprintf "%.1f" s.OutAltM
                        sprintf "%.1f" s.RetAltM
                        String.Join(";", s.DropsAt |> List.map (sprintf "%.0f"))
                    ]
        ]
