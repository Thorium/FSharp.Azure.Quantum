/// Evidence pack for a one-remote-pilot-to-many-aircraft (1:N) permission.
///
/// Flying one pilot per aircraft is the default. An operator asking for 1:N
/// must show, for the operation as planned, that the aircraft:
///   - stay DECONFLICTED from each other,
///   - stay IN RANGE: of their batteries (with reserve) and of the C2 link,
///   - stay under the ALTITUDE CEILING,
///   - and what happens when ONE DROPS OUT (failure, lost link, low battery),
/// plus the pilot-to-aircraft ratio actually being asked for, and the
/// SUPERVISOR WORKLOAD behind it: the aircraft fly their plans on their own,
/// so the pilot's job is the exceptions. Each contingency scenario is a set of
/// off-nominal events, each resolved automatically (a failsafe, a re-plan) or
/// needing a pilot decision; the ratio holds when no moment asks for more
/// decisions at once than there are pilots.
///
/// Every drone example builds one of these from its own plan and writes it
/// next to its other outputs. Each check says what was measured, how, and
/// against which limit. Anything the model cannot show is reported as
/// NOT EVIDENCED rather than passed: a gap an assessor finds is worse than a
/// gap the operator declared.
///
/// The limits come from Domain (DroneDomain.fs); they are the example's
/// working values, not a statement of any authority's rule. This is decision
/// support for building a safety case, not a substitute for one.
module FSharp.Azure.Quantum.Examples.Drones.PermissionEvidence

open System
open System.IO

open FSharp.Azure.Quantum.Examples.Common
open FSharp.Azure.Quantum.Examples.Drones.Domain

// =============================================================================
// TYPES
// =============================================================================

type Area =
    | Deconfliction
    | EnduranceRange
    | C2Link
    | AltitudeCeiling
    | Contingency
    | PilotRatio
    | SupervisorWorkload

type Status =
    | Pass
    | Fail
    /// The model cannot show this; it must come from elsewhere in the case.
    | NotEvidenced
    /// A declared fact (e.g. the ratio asked for), not a pass/fail test.
    | Declared

type Check =
    {
        Area: Area
        Claim: string
        Method: string
        Measured: string
        Limit: string
        Status: Status
        /// Worst offenders or supporting detail, most relevant first.
        Details: string list
    }

/// Who resolves an off-nominal event.
type Handling =
    /// A failsafe or the planner resolves it without a person.
    | Automatic
    /// A person has to decide or act.
    | PilotDecision

/// One off-nominal event in a contingency scenario.
type OffNominal =
    {
        /// What happens, e.g. "UAV001 drops out at t=812 s".
        Event: string
        /// Aircraft off-nominal because of it.
        Affected: int
        StartS: float
        /// How long the aircraft are off-plan, or a decision holds the pilot.
        DurationS: float
        Handling: Handling
        Response: string
    }

/// Working allowance for one pilot decision (assess, decide, command).
let decisionTimeS = 60.0

type Pack =
    {
        Example: string
        Operation: string
        Pilots: int
        Aircraft: int
        PeakAirborne: int
        Checks: Check list
        Assumptions: string list
    }

let areaName =
    function
    | Deconfliction -> "Deconfliction"
    | EnduranceRange -> "Endurance range"
    | C2Link -> "C2 link range"
    | AltitudeCeiling -> "Altitude ceiling"
    | Contingency -> "Contingency"
    | PilotRatio -> "Pilot-to-aircraft ratio"
    | SupervisorWorkload -> "Supervisor workload"

let statusName =
    function
    | Pass -> "PASS"
    | Fail -> "FAIL"
    | NotEvidenced -> "NOT EVIDENCED"
    | Declared -> "DECLARED"

let private allAreas =
    [
        Deconfliction
        EnduranceRange
        C2Link
        AltitudeCeiling
        Contingency
        PilotRatio
        SupervisorWorkload
    ]

/// FAIL if anything failed; INCOMPLETE if an area has no check or something is
/// not evidenced; otherwise PASS.
let verdict (pack: Pack) =
    let statuses = pack.Checks |> List.map (fun c -> c.Status)
    let covered = pack.Checks |> List.map (fun c -> c.Area) |> Set.ofList

    if statuses |> List.contains Fail then
        "FAIL"
    elif statuses |> List.contains NotEvidenced then
        "INCOMPLETE"
    elif allAreas |> List.exists (covered.Contains >> not) then
        "INCOMPLETE"
    else
        "PASS"

let private pass ok = if ok then Pass else Fail

// =============================================================================
// GEOMETRY: TRACKS AND SEPARATION
// =============================================================================

/// Local position in metres (X east, Y north, Z up above ground level).
type P3 = { X: float; Y: float; Z: float }

let dist3 (a: P3) (b: P3) =
    Math.Sqrt((a.X - b.X) ** 2.0 + (a.Y - b.Y) ** 2.0 + (a.Z - b.Z) ** 2.0)

/// A time-stamped path of one aircraft. Times in seconds, ascending. Before
/// its first and after its last sample the aircraft is not airborne.
type Track =
    {
        AircraftId: string
        Samples: (float * P3)[]
    }

/// Position at time t by linear interpolation; None when not airborne.
let positionAt (track: Track) (t: float) : P3 option =
    let s = track.Samples

    if s.Length = 0 || t < fst s.[0] || t > fst s.[s.Length - 1] then
        None
    else
        let i =
            s
            |> Array.tryFindIndex (fun (ti, _) -> ti >= t)
            |> Option.defaultValue (s.Length - 1)

        if i = 0 then
            Some(snd s.[0])
        else
            let t0, p0 = s.[i - 1]
            let t1, p1 = s.[i]
            let f = if t1 > t0 then (t - t0) / (t1 - t0) else 0.0

            Some
                {
                    X = p0.X + f * (p1.X - p0.X)
                    Y = p0.Y + f * (p1.Y - p0.Y)
                    Z = p0.Z + f * (p1.Z - p0.Z)
                }

type Closest =
    {
        Distance: float
        A: string
        B: string
        TimeS: float
        Where: P3
    }

/// Exact closest approach of two tracks. Between consecutive breakpoints (the
/// union of both tracks' sample times) both move in straight lines at constant
/// velocity, so their separation is |d0 + s (d1 - d0)| for s in [0, 1], whose
/// minimum has a closed form. No sampling: two aircraft leaving one pad at the
/// same instant are 0 m apart, not "one step's flight" apart. Intervals where
/// both are below `groundZ` (parked) are skipped.
let private closestPair (groundZ: float) (a: Track) (b: Track) : Closest option =
    let t0 = max (fst a.Samples.[0]) (fst b.Samples.[0])
    let t1 = min (fst (Array.last a.Samples)) (fst (Array.last b.Samples))

    if t0 > t1 then
        None
    else
        let breaks =
            Array.append (Array.map fst a.Samples) (Array.map fst b.Samples)
            |> Array.filter (fun t -> t > t0 && t < t1)
            |> Array.append [| t0; t1 |]
            |> Array.sort
            |> Array.distinct

        let intervals =
            if breaks.Length = 1 then
                [| (t0, t0) |]
            else
                Array.pairwise breaks

        intervals
        |> Array.choose (fun (ta, tb) ->
            match positionAt a ta, positionAt a tb, positionAt b ta, positionAt b tb with
            | Some a0, Some a1, Some b0, Some b1 when not (max a0.Z a1.Z < groundZ && max b0.Z b1.Z < groundZ) ->
                let d0 =
                    {
                        X = a0.X - b0.X
                        Y = a0.Y - b0.Y
                        Z = a0.Z - b0.Z
                    }

                let v =
                    {
                        X = (a1.X - b1.X) - d0.X
                        Y = (a1.Y - b1.Y) - d0.Y
                        Z = (a1.Z - b1.Z) - d0.Z
                    }

                let vv = v.X * v.X + v.Y * v.Y + v.Z * v.Z

                let s =
                    if vv < 1e-12 then
                        0.0
                    else
                        Math.Clamp(-(d0.X * v.X + d0.Y * v.Y + d0.Z * v.Z) / vv, 0.0, 1.0)

                let rel =
                    {
                        X = d0.X + s * v.X
                        Y = d0.Y + s * v.Y
                        Z = d0.Z + s * v.Z
                    }

                Some
                    {
                        Distance = Math.Sqrt(rel.X * rel.X + rel.Y * rel.Y + rel.Z * rel.Z)
                        A = a.AircraftId
                        B = b.AircraftId
                        TimeS = ta + s * (tb - ta)
                        Where =
                            {
                                X = a0.X + s * (a1.X - a0.X)
                                Y = a0.Y + s * (a1.Y - a0.Y)
                                Z = a0.Z + s * (a1.Z - a0.Z)
                            }
                    }
            | _ -> None)
        |> Array.sortBy (fun c -> c.Distance)
        |> Array.tryHead

/// Smallest distance between any two airborne aircraft, computed exactly for
/// straight-line tracks (see closestPair). `stepS` is no longer used for the
/// distance, which has no sampling gap; it is kept so callers need not change.
let closestApproach (stepS: float) (groundZ: float) (tracks: Track list) : Closest option =
    ignore stepS
    let tracks = tracks |> List.filter (fun t -> t.Samples.Length > 0) |> Array.ofList

    [
        for i in 0 .. tracks.Length - 1 do
            for j in i + 1 .. tracks.Length - 1 do
                match closestPair groundZ tracks.[i] tracks.[j] with
                | Some c -> c
                | None -> ()
    ]
    |> List.sortBy (fun c -> c.Distance)
    |> List.tryHead

/// Most aircraft airborne at the same time.
let peakAirborne (stepS: float) (tracks: Track list) : int =
    match tracks |> List.filter (fun t -> t.Samples.Length > 0) with
    | [] -> 0
    | ts ->
        let tEnd = ts |> List.map (fun t -> fst (Array.last t.Samples)) |> List.max
        let tStart = ts |> List.map (fun t -> fst t.Samples.[0]) |> List.min

        Seq.initInfinite (fun k -> tStart + float k * stepS)
        |> Seq.takeWhile (fun t -> t <= tEnd + 1e-9)
        |> Seq.map (fun t -> ts |> List.filter (fun tr -> (positionAt tr t).IsSome) |> List.length)
        |> Seq.max

// =============================================================================
// C2 LINK BUDGET
// =============================================================================

module C2 =

    /// Planning range of the command-and-control link: free-space (Friis)
    /// range from the domain's typical radio, minus a fade margin, capped at
    /// the typical BVLOS approval limit. Free space is optimistic near terrain
    /// and trees, which is what the margin is for.
    let rangeKm (frequencyMhz: float) (fadeMarginDb: float) =
        let friis =
            calculateTheoreticalRange
                Communication.typicalTxPowerDbm
                Communication.typicalAntennaGainDbi
                Communication.typicalAntennaGainDbi
                (Communication.typicalRxSensitivityDbm + fadeMarginDb)
                frequencyMhz

        min friis Regulations.typicalBvlosRangeLimitKm

    let describe (frequencyMhz: float) (fadeMarginDb: float) =
        sprintf
            "%.0f MHz, %.0f dBm Tx, %.0f dBi antennas, %.0f dBm sensitivity, %.0f dB fade margin, capped at %.0f km"
            frequencyMhz
            Communication.typicalTxPowerDbm
            Communication.typicalAntennaGainDbi
            Communication.typicalRxSensitivityDbm
            fadeMarginDb
            Regulations.typicalBvlosRangeLimitKm

// =============================================================================
// STANDARD CHECKS
// =============================================================================

module Checks =

    let separation (minSepM: float) (method: string) (closest: Closest option) =
        match closest with
        | None ->
            {
                Area = Deconfliction
                Claim = "No two aircraft are ever closer than the minimum separation"
                Method = method
                Measured = "fewer than two aircraft airborne together"
                Limit = sprintf ">= %.1f m" minSepM
                Status = Pass
                Details = []
            }
        | Some c ->
            {
                Area = Deconfliction
                Claim = "No two aircraft are ever closer than the minimum separation"
                Method = method
                Measured = sprintf "closest approach %.1f m (%s / %s at t=%.0f s)" c.Distance c.A c.B c.TimeS
                Limit = sprintf ">= %.1f m" minSepM
                Status = pass (c.Distance >= minSepM)
                Details =
                    [
                        sprintf "at local position (%.0f, %.0f, %.0f) m" c.Where.X c.Where.Y c.Where.Z
                    ]
            }

    /// `altitudes`: (what, metres AGL) for every planned point.
    let altitude (altitudes: (string * float) list) =
        let ceiling = Regulations.maxAltitudeAglMeters

        let over =
            altitudes
            |> List.filter (fun (_, a) -> a > ceiling)
            |> List.sortByDescending snd

        let highest = altitudes |> List.map snd |> List.fold max Double.NegativeInfinity

        {
            Area = AltitudeCeiling
            Claim = "Every planned point is at or below the altitude ceiling"
            Method = sprintf "maximum over %d planned points" altitudes.Length
            Measured =
                if altitudes.IsEmpty then
                    "no points"
                else
                    sprintf "highest %.1f m AGL" highest
            Limit = sprintf "<= %.1f m AGL" ceiling
            Status = pass over.IsEmpty
            Details = over |> List.truncate 10 |> List.map (fun (w, a) -> sprintf "%s at %.1f m" w a)
        }

    /// `distancesKm`: (what, horizontal distance from the pilot station).
    let c2Link (stationName: string) (frequencyMhz: float) (fadeMarginDb: float) (distancesKm: (string * float) list) =
        let range = C2.rangeKm frequencyMhz fadeMarginDb
        let worst = distancesKm |> List.sortByDescending snd

        {
            Area = C2Link
            Claim = sprintf "Every planned point is within C2 link range of %s" stationName
            Method =
                "horizontal distance to the station vs. "
                + C2.describe frequencyMhz fadeMarginDb
            Measured =
                match worst with
                | (w, d) :: _ -> sprintf "farthest %.2f km (%s)" d w
                | [] -> "no points"
            Limit = sprintf "<= %.2f km" range
            Status = pass (worst |> List.forall (fun (_, d) -> d <= range))
            Details =
                worst
                |> List.filter (fun (_, d) -> d > range)
                |> List.truncate 10
                |> List.map (fun (w, d) -> sprintf "%s at %.2f km" w d)
        }

    /// `legs`: (aircraft, needed, available) in the same unit, where
    /// `available` already excludes the battery reserve.
    let endurance (unit: string) (method: string) (legs: (string * float * float) list) =
        let short = legs |> List.filter (fun (_, need, have) -> need > have)

        let tightest =
            legs
            |> List.sortBy (fun (_, need, have) -> if have > 0.0 then (have - need) / have else -1.0)

        {
            Area = EnduranceRange
            Claim =
                sprintf
                    "Every aircraft completes its flying with the %.0f%% battery reserve intact"
                    Battery.reserveBatteryPercent
            Method = method
            Measured =
                match tightest with
                | (a, need, have) :: _ -> sprintf "tightest %s: needs %.1f of %.1f %s usable" a need have unit
                | [] -> "no flights"
            Limit = "needed <= usable (usable excludes reserve)"
            Status = pass short.IsEmpty
            Details =
                short
                |> List.map (fun (a, need, have) -> sprintf "%s needs %.1f %s, has %.1f" a need unit have)
        }

    let pilotRatio (pilots: int) (aircraft: int) (peakAirborne: int) =
        {
            Area = PilotRatio
            Claim = "Pilot-to-aircraft ratio requested"
            Method = "declared pilots vs. fleet size and peak simultaneously airborne"
            Measured =
                sprintf
                    "1:%d fleet, 1:%d airborne at peak"
                    (aircraft / max 1 pilots)
                    (int (Math.Ceiling(float peakAirborne / float (max 1 pilots))))
            Limit = "as granted in the permission"
            Status = Declared
            Details =
                [
                    sprintf "%d pilot(s), %d aircraft, peak %d airborne" pilots aircraft peakAirborne
                ]
        }

    /// Supervisor workload over the operation's contingency scenarios. Each
    /// scenario is the set of off-nominal events one contingency produces; the
    /// scenarios are assessed separately (their events overlap only within a
    /// scenario). A pilot handles one decision at a time: the operation holds
    /// when, at every moment of every scenario, the decisions needed at once fit
    /// the pilots declared. Automatic events do not load a pilot, but they are
    /// counted: many aircraft off-nominal at once is the house of cards this
    /// check exists to show, even when every one resolves itself.
    let workload (pilots: int) (scenarios: (string * OffNominal list) list) =
        let peak (events: OffNominal list) (weight: OffNominal -> int) =
            events
            |> List.collect (fun e -> [ (e.StartS, weight e); (e.StartS + e.DurationS, -(weight e)) ])
            // At the same instant an event ends before the next begins.
            |> List.sortBy (fun (t, w) -> (t, w))
            |> List.scan (fun acc (_, w) -> acc + w) 0
            |> List.max

        let assessed =
            scenarios
            |> List.map (fun (name, events) ->
                let aircraft = peak events (fun e -> e.Affected)

                let decisions = peak events (fun e -> if e.Handling = PilotDecision then 1 else 0)

                (name, events, aircraft, decisions))

        let worstAircraft =
            assessed |> List.sortByDescending (fun (_, _, a, _) -> a) |> List.tryHead

        let worstDecisions =
            assessed |> List.sortByDescending (fun (_, _, _, d) -> d) |> List.tryHead

        let handsOff = assessed |> List.filter (fun (_, _, _, d) -> d = 0) |> List.length

        {
            Area = SupervisorWorkload
            Claim = "No contingency asks more of the pilots at once than there are pilots"
            Method =
                "per contingency scenario, a sweep over its off-nominal events: aircraft off-nominal at once, and pilot decisions needed at once (a decision holds the pilot for its stated duration); automatic events are failsafes or re-plans that resolve without a person"
            Measured =
                match worstAircraft, worstDecisions with
                | Some(na, _, a, _), Some(nd, _, _, d) ->
                    sprintf
                        "up to %d aircraft off-nominal at once (%s); %s; %d of %d scenarios hands-off"
                        a
                        na
                        (if d = 0 then
                             "no pilot decisions needed"
                         else
                             sprintf "up to %d decision(s) at once (%s)" d nd)
                        handsOff
                        assessed.Length
                | _ -> "no contingency scenarios"
            Limit = sprintf "decisions at once <= %d pilot(s)" pilots
            Status =
                if assessed |> List.forall (fun (_, _, _, d) -> d <= pilots) then
                    Pass
                else
                    Fail
            Details =
                assessed
                |> List.sortByDescending (fun (_, _, a, d) -> (d, a))
                // Dropouts a few seconds apart read the same: one line each, with a count.
                |> List.groupBy (fun (_, events, a, d) ->
                    (a, d, events |> List.map (fun e -> (e.Handling, e.Response))))
                |> List.map (fun (_, group) ->
                    let name, events, a, d = List.head group

                    let name =
                        if group.Length > 1 then
                            sprintf "%s (and %d similar)" name (group.Length - 1)
                        else
                            name

                    (name, events, a, d))
                |> List.truncate 12
                |> List.map (fun (name, events, a, d) ->
                    let how =
                        events
                        |> List.map (fun e ->
                            sprintf
                                "%s -> %s (%s)"
                                e.Event
                                e.Response
                                (if e.Handling = Automatic then
                                     "automatic"
                                 else
                                     "PILOT DECISION"))
                        |> List.distinct
                        |> List.truncate 3
                        |> String.concat "; "

                    sprintf "%s: %d aircraft, %d decision(s) at once — %s" name a d how)
        }

    let contingency (claim: string) (method: string) (measured: string) (status: Status) (details: string list) =
        {
            Area = Contingency
            Claim = claim
            Method = method
            Measured = measured
            Limit = "operation stays within the other checks"
            Status = status
            Details = details
        }

    let notEvidenced (area: Area) (claim: string) (why: string) =
        {
            Area = area
            Claim = claim
            Method = "-"
            Measured = "-"
            Limit = "-"
            Status = NotEvidenced
            Details = [ why ]
        }

// =============================================================================
// OUTPUT
// =============================================================================

let print (pack: Pack) =
    printfn ""
    printfn "── 1:N PERMISSION EVIDENCE: %s — %s ──" (verdict pack) pack.Operation

    for c in pack.Checks do
        let what = if c.Status = NotEvidenced then c.Claim else c.Measured
        printfn "  %-14s %-28s %s" (statusName c.Status) (areaName c.Area) what

    printfn "  (full pack: permission-evidence.md)"

let private toMarkdown (pack: Pack) =
    let sb = Text.StringBuilder()
    let line (s: string) = sb.AppendLine s |> ignore

    line (sprintf "# One-pilot-to-many permission evidence — %s" pack.Example)
    line ""
    line (sprintf "**Operation:** %s  " pack.Operation)
    line (sprintf "**Overall:** %s  " (verdict pack))

    line (
        sprintf "**Pilots:** %d · **Aircraft:** %d · **Peak airborne:** %d" pack.Pilots pack.Aircraft pack.PeakAirborne
    )

    line ""
    line "| Area | Status | Claim | Measured | Limit |"
    line "|---|---|---|---|---|"

    for c in pack.Checks do
        line (sprintf "| %s | %s | %s | %s | %s |" (areaName c.Area) (statusName c.Status) c.Claim c.Measured c.Limit)

    for area in allAreas do
        let checks = pack.Checks |> List.filter (fun c -> c.Area = area)
        line ""
        line (sprintf "## %s" (areaName area))

        if checks.IsEmpty then
            line ""
            line "**NOT EVIDENCED** — this example produces no evidence for this area."

        for c in checks do
            line ""
            line (sprintf "**%s** — %s" (statusName c.Status) c.Claim)
            line ""
            line (sprintf "- Method: %s" c.Method)
            line (sprintf "- Measured: %s" c.Measured)
            line (sprintf "- Limit: %s" c.Limit)

            for d in c.Details do
                line (sprintf "  - %s" d)

    line ""
    line "## Assumptions"
    line ""

    for a in pack.Assumptions do
        line (sprintf "- %s" a)

    line ""
    line "Limits are this example's working values from DroneDomain.fs, not any authority's rule."
    line "This pack is decision support for a safety case, not a substitute for one."
    sb.ToString()

let write (outDir: string) (pack: Pack) =
    Reporting.writeTextFile (Path.Combine(outDir, "permission-evidence.md")) (toMarkdown pack)

    Reporting.writeJson
        (Path.Combine(outDir, "permission-evidence.json"))
        {|
            example = pack.Example
            operation = pack.Operation
            verdict = verdict pack
            pilots = pack.Pilots
            aircraft = pack.Aircraft
            peak_airborne = pack.PeakAirborne
            checks =
                pack.Checks
                |> List.map (fun c ->
                    {|
                        area = areaName c.Area
                        status = statusName c.Status
                        claim = c.Claim
                        methodology = c.Method
                        measured = c.Measured
                        limit = c.Limit
                        details = c.Details
                    |})
            assumptions = pack.Assumptions
        |}
