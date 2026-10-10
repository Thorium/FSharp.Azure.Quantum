// Copyright (c) 2025 FSharp.Azure.Quantum Contributors
// SPDX-License-Identifier: MIT

/// An animated picture of a light show: every drone is a dot in the colour its light has
/// on, in a 3-D view from the south-west and a view from the south, holding each formation
/// and flying straight to the next, looping. Plain SVG with SMIL animation, so it plays in
/// a browser and as an image in a README. A drone's light changes when it leaves for a
/// slot, as the Crazyflie script sets it; a black light is drawn as a dark dot so the drone
/// is still seen to be there.
module FSharp.Azure.Quantum.Examples.Drones.SwarmChoreography.LightShowPicture

open System
open System.Globalization
open System.IO
open System.Text

/// One formation as flown: the drones' positions (metres, X east, Y north, Z up) and
/// lights, by drone.
type Frame =
    {
        Name: string
        Positions: (float * float * float)[]
        Lights: (int * int * int)[]
        /// The drones pass through without holding: a step of one continuous move
        Flow: bool
    }

let private inv = CultureInfo.InvariantCulture
let private num (v: float) = v.ToString("0.##", inv)
let private hex (r: int, g: int, b: int) = sprintf "#%02x%02x%02x" r g b

/// Share of a frame's time the formation is held before the drones leave for the next.
[<Literal>]
let Hold = 0.55

/// Seconds a frame that only changes the lights (the drones hold) is on screen, so a
/// pattern turning in many steps plays as a turn and not as a row of formations.
[<Literal>]
let LightStepS = 0.15

/// Seconds of flight into or out of a frame the drones flow through, so a deformation or
/// a turn in many small steps plays as one move.
[<Literal>]
let FlowStepS = 0.3

/// Writes `frames` to `path`: `secondsPerFrame` for each formation the drones fly to (held
/// for Hold of it, then the flight to the next), LightStepS for one they hold through, and
/// no hold at a frame the drones flow through (the flight to the next starts on arrival
/// and takes FlowStepS, as does the flight into it).
let write (path: string) (title: string) (secondsPerFrame: float) (frames: Frame[]) : unit =
    if frames.Length = 0 then
        invalidArg "frames" "a picture needs at least one formation"

    let n = frames.Length
    let drones = frames.[0].Positions.Length
    let width, height = 1000.0, 560.0
    let panelTop, panelHeight = 56.0, 450.0
    let panelWidth = (width - 3.0 * 20.0) / 2.0

    // The 3-D view: orthographic, from the south-west and above
    let az, el = 35.0 * Math.PI / 180.0, 22.0 * Math.PI / 180.0

    let oblique (x: float, y: float, z: float) =
        x * cos az + y * sin az, -(x * sin az * sin el) + y * cos az * sin el + z * cos el

    let front (x: float, _: float, z: float) = x, z

    // Each view fitted to its panel over every frame, the same scale in both axes
    let fit (project: float * float * float -> float * float) (left: float) =
        let points = frames |> Array.collect (fun f -> f.Positions |> Array.map project)
        let xs, ys = points |> Array.map fst, points |> Array.map snd
        let minX, maxX, minY, maxY = Array.min xs, Array.max xs, Array.min ys, Array.max ys
        let margin = 30.0

        let scale =
            min ((panelWidth - 2.0 * margin) / max 1.0 (maxX - minX)) ((panelHeight - 2.0 * margin) / max 1.0 (maxY - minY))

        let offsetX = left + (panelWidth - scale * (maxX - minX)) / 2.0
        let offsetY = panelTop + (panelHeight - scale * (maxY - minY)) / 2.0

        fun (p: float * float * float) ->
            let x, y = project p
            offsetX + scale * (x - minX), offsetY + scale * (maxY - y)

    let views =
        [
            "3-D view from the south-west", fit oblique 20.0
            "From the south (east to the right)", fit front (40.0 + panelWidth)
        ]

    // Whether the drones fly to frame k, or only change their lights there
    let flown (k: int) = k > 0 && frames.[k].Positions <> frames.[k - 1].Positions

    // Whether the drones leave frame k the moment they arrive
    let flows (k: int) = k > 0 && k < n - 1 && frames.[k].Flow && flown (k + 1)

    // The timeline: a frame the drones fly to holds for Hold of its seconds when the next
    // is flown to (or it is the last), else a light step; a frame reached by a light step
    // holds one light step whatever follows, so a turn of the lights ends and the flight
    // begins without a pause; a frame flown through holds nothing
    let arriveS = Array.zeroCreate<float> n
    let departS = Array.zeroCreate<float> n

    for k in 0 .. n - 1 do
        let nextFlown = k < n - 1 && flown (k + 1)

        departS.[k] <-
            arriveS.[k]
            + (if flows k then 0.0
               elif k = n - 1 then secondsPerFrame * Hold
               elif nextFlown && (k = 0 || flown k) then secondsPerFrame * Hold
               else LightStepS)

        if k < n - 1 then
            arriveS.[k + 1] <-
                departS.[k]
                + (if not nextFlown then 0.0
                   elif flows k || flows (k + 1) then FlowStepS
                   else secondsPerFrame * (1.0 - Hold))

    let duration = departS.[n - 1]
    let key (t: float) = (t / duration).ToString("0.###", inv)
    let arrive (k: int) = key arriveS.[k]
    let depart (k: int) = key departS.[k]

    // Motion needs a key only where a frame is flown to or from; between them the drones
    // hold through any number of light steps. A frame flown through is one key, not a
    // held pair.
    let motionFrames =
        [ 0 .. n - 1 ]
        |> List.filter (fun k -> k = 0 || k = n - 1 || flown k || flown (k + 1))

    let motionKeys =
        String.Join(
            ";",
            [
                for k in motionFrames do
                    yield arrive k

                    if not (flows k) then
                        yield depart k
            ]
        )

    let svg = StringBuilder()
    let line (s: string) = svg.Append(s).Append('\n') |> ignore

    line (
        sprintf
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"%s\" height=\"%s\" viewBox=\"0 0 %s %s\" font-family=\"Helvetica, Arial, sans-serif\">"
            (num width)
            (num height)
            (num width)
            (num height)
    )

    line (sprintf "<title>%s</title>" (System.Security.SecurityElement.Escape title))
    line (sprintf "<rect width=\"%s\" height=\"%s\" fill=\"#0b0f1a\"/>" (num width) (num height))

    line (
        sprintf
            "<text x=\"20\" y=\"32\" font-size=\"18\" font-weight=\"bold\" fill=\"#e5e5e5\">%s</text>"
            (System.Security.SecurityElement.Escape title)
    )

    // What is on show: the formation while it is held, then the move to the next, whose
    // lights the drones already carry
    let label (text: string) (from: string) (until: string option) =
        let keys, values =
            match from, until with
            | "0", None -> "", ""
            | "0", Some u -> sprintf "0;%s" u, "visible;hidden"
            | f, None -> sprintf "0;%s" f, "hidden;visible"
            | f, Some u -> sprintf "0;%s;%s" f u, "hidden;visible;hidden"

        line (
            sprintf
                "<text x=\"%s\" y=\"32\" font-size=\"16\" fill=\"#9ecbff\" text-anchor=\"end\" visibility=\"%s\">%s%s</text>"
                (num (width - 20.0))
                (if from = "0" then "visible" else "hidden")
                (System.Security.SecurityElement.Escape text)
                (if keys = "" then
                     ""
                 else
                     sprintf
                         "<animate attributeName=\"visibility\" dur=\"%ss\" repeatCount=\"indefinite\" calcMode=\"discrete\" keyTimes=\"%s\" values=\"%s\"/>"
                         (num duration)
                         keys
                         values)
        )

    for k in 0 .. n - 1 do
        let name = frames.[k].Name

        if n = 1 then
            label name "0" None
        elif k = n - 1 then
            label (sprintf "%d / %d: %s" (k + 1) n name) (arrive k) None
        elif flows k then
            label (sprintf "%s → %s" name frames.[k + 1].Name) (arrive k) (Some(arrive (k + 1)))
        elif flown (k + 1) then
            label (sprintf "%d / %d: %s" (k + 1) n name) (arrive k) (Some(depart k))
            label (sprintf "%s → %s" name frames.[k + 1].Name) (depart k) (Some(arrive (k + 1)))
        else
            label (sprintf "%d / %d: %s" (k + 1) n name) (arrive k) (Some(arrive (k + 1)))

    for label, place in views do
        line (
            sprintf
                "<text x=\"%s\" y=\"%s\" font-size=\"12\" fill=\"#8a93a6\">%s</text>"
                (num (if label.StartsWith "3-D" then 20.0 else 40.0 + panelWidth))
                (num (panelTop - 6.0))
                label
        )

        for d in 0 .. drones - 1 do
            let where = frames |> Array.map (fun f -> place f.Positions.[d])
            let lights = frames |> Array.map (fun f -> hex f.Lights.[d])
            let at (x: float, y: float) = num x + " " + num y

            // One group per drone carries the motion and the light; its halo (a soft disc in
            // the light's colour, black on black for a light that is off) and the drone
            // itself, outlined so a dark light is still a drone, inherit both
            let motion =
                if n = 1 then
                    ""
                else
                    sprintf
                        "<animateTransform attributeName=\"transform\" type=\"translate\" dur=\"%ss\" repeatCount=\"indefinite\" calcMode=\"linear\" keyTimes=\"%s\" values=\"%s\"/>"
                        (num duration)
                        motionKeys
                        (String.Join(
                            ";",
                            [
                                for k in motionFrames do
                                    yield at where.[k]

                                    if not (flows k) then
                                        yield at where.[k]
                            ]
                        ))

            // The light changes when the drone leaves for a frame lit otherwise; only those
            // changes are keyed, so a drone lit the same through many frames costs nothing
            let changes =
                [
                    yield "0", lights.[0]

                    for k in 1 .. n - 1 do
                        if lights.[k] <> lights.[k - 1] then
                            yield depart (k - 1), lights.[k]
                ]

            let light =
                if changes.Length = 1 then
                    ""
                else
                    sprintf
                        "<animate attributeName=\"fill\" dur=\"%ss\" repeatCount=\"indefinite\" calcMode=\"discrete\" keyTimes=\"%s\" values=\"%s\"/>"
                        (num duration)
                        (String.Join(";", changes |> List.map fst))
                        (String.Join(";", changes |> List.map snd))

            line (
                sprintf
                    "<g transform=\"translate(%s)\" fill=\"%s\">%s%s<circle r=\"7\" opacity=\"0.3\"/><circle r=\"3.2\" stroke=\"#3a4150\" stroke-width=\"0.6\"/></g>"
                    (at where.[0])
                    lights.[0]
                    motion
                    light
            )

    line "</svg>"
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath path)) |> ignore
    File.WriteAllText(path, svg.ToString())
    printfn "Drawn: %s (animated, %s s loop, %d formations)" path (num duration) n
