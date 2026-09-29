/// Animated SVG pictures for the examples: plain SVG plus SMIL animation, no
/// packages. A picture is a fixed page, and anything on it can change over
/// `frames` steps of an example's own computed data. The whole run plays in
/// `durationS` seconds and loops. Each frame holds still for `hold` of its
/// share of the time, then moves to the next; a step picture (`hold` >= 0.5)
/// also gets `readS` (1 s) more on every frame to read it. It plays in any
/// browser, including as an image in a README on GitHub.
///
///     #load "../_common/SvgAnimation.fsx"
///     open SvgAnimation
///     let pic = Picture(760.0, 420.0, frames = 12, durationS = 18.0, title = "…")
///     pic.Rect(40.0, 60.0, 30.0, 0.0, fill = colour 0, animate = [ "height", heights; "y", tops ])
///     pic.FrameText(40.0, 30.0, [| for k in 0 .. 11 -> sprintf "step %d" k |])
///     pic.Save "picture.svg"
///
/// `animate` takes, per attribute, one value per frame: numbers, and paths with
/// the same commands in every frame, move smoothly between frames; anything
/// else changes at the frame's start.
module SvgAnimation

open System
open System.Globalization
open System.IO
open System.Text

let private inv = CultureInfo.InvariantCulture

/// A number as SVG wants it: at most two decimals.
let num (v: float) = v.ToString("0.##", inv)

let private escape (s: string) =
    s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")

/// The drone pictures' colours: ten distinct ones, then hues by the golden angle.
let private palette =
    [|
        "#1f77b4"
        "#d62728"
        "#2ca02c"
        "#9467bd"
        "#ff7f0e"
        "#17becf"
        "#8c564b"
        "#e377c2"
        "#7f7f7f"
        "#bcbd22"
    |]

let colour (i: int) =
    if i < palette.Length then
        palette.[i]
    else
        let h = (float (i - palette.Length) * 137.508) % 360.0
        // HSL(h, 60%, 42%) as RGB.
        let c = (1.0 - abs (2.0 * 0.42 - 1.0)) * 0.6
        let x = c * (1.0 - abs ((h / 60.0) % 2.0 - 1.0))
        let m = 0.42 - c / 2.0

        let r, g, b =
            match int (h / 60.0) with
            | 0 -> (c, x, 0.0)
            | 1 -> (x, c, 0.0)
            | 2 -> (0.0, c, x)
            | 3 -> (0.0, x, c)
            | 4 -> (x, 0.0, c)
            | _ -> (c, 0.0, x)

        sprintf "#%02x%02x%02x" (int ((r + m) * 255.0)) (int ((g + m) * 255.0)) (int ((b + m) * 255.0))

/// Text and frame colours.
let ink, grey, light, panel, frameColour =
    "#1a1a1a", "#555555", "#e5e5e5", "#fafafa", "#cccccc"

/// The pictures' typeface, set on each text element: some SVG renderers do
/// not inherit it from the root.
[<Literal>]
let fontFamily = "Helvetica, Arial, sans-serif"

/// Blend `hex` toward white by `f` (0 = the colour, 1 = white).
let tint (f: float) (hex: string) =
    let c (i: int) =
        Convert.ToInt32(hex.Substring(i, 2), 16)

    let mix v = int (float v + (255.0 - float v) * f)
    sprintf "#%02x%02x%02x" (mix (c 1)) (mix (c 3)) (mix (c 5))

/// A step picture (each frame held for at least half its share, `hold` >= 0.5)
/// gets `readS` more seconds on every frame, all of it spent holding the frame,
/// so there is time to read it before it moves on; the moves between frames
/// keep their speed. Continuous motion (a smaller `hold`) keeps its pace.
type Picture(width: float, height: float, frames: int, durationS: float, title: string, ?hold: float, ?readS: float) =
    do
        if frames < 1 then
            invalidArg "frames" "a picture needs at least one frame"

    let requestedHold = defaultArg hold 0.6

    let readS = if requestedHold >= 0.5 then defaultArg readS 1.0 else 0.0

    let slotS = durationS / float frames
    let durationS = durationS + readS * float frames

    let hold = (requestedHold * slotS + readS) / (slotS + readS)

    let body = StringBuilder()

    let isNumber (s: string) =
        Double.TryParse(s, NumberStyles.Float, inv) |> fst

    let numberPattern = RegularExpressions.Regex("-?[0-9]*\\.?[0-9]+(e-?[0-9]+)?")

    // A path or point list with the same commands and as many numbers in
    // every frame morphs smoothly: only the numbers change.
    let colourPattern = RegularExpressions.Regex("^#[0-9a-fA-F]{3,8}$")

    let sameShape (values: string[]) =
        let skeleton (v: string) = numberPattern.Replace(v, "#")
        let first = skeleton values.[0]

        first.Contains '#'
        && not (values |> Array.exists colourPattern.IsMatch)
        && values |> Array.forall (fun v -> skeleton v = first)

    // One value per frame: smooth (numbers, or paths of one shape, holding
    // each frame then moving to the next) or discrete (anything else,
    // changing at each frame's start).
    let animation (attribute: string) (values: string[]) =
        if values.Length <> frames then
            invalidArg attribute $"%d{values.Length} values for %d{frames} frames"

        let n = float frames
        let dur = sprintf "%ss" (num durationS)

        if frames > 1 && (values |> Array.forall isNumber || sameShape values) then
            let keys = ResizeArray<float * string>()

            for i in 0 .. frames - 1 do
                keys.Add((float i / n, values.[i]))
                keys.Add(((float i + hold) / n, values.[i]))

            keys.Add((1.0, values.[frames - 1]))

            sprintf
                "<animate attributeName=\"%s\" dur=\"%s\" repeatCount=\"indefinite\" calcMode=\"linear\" keyTimes=\"%s\" values=\"%s\"/>"
                attribute
                dur
                (String.Join(";", keys |> Seq.map (fst >> fun t -> t.ToString("0.####", inv))))
                (String.Join(";", keys |> Seq.map snd))
        else
            sprintf
                "<animate attributeName=\"%s\" dur=\"%s\" repeatCount=\"indefinite\" calcMode=\"discrete\" keyTimes=\"%s\" values=\"%s\"/>"
                attribute
                dur
                (String.Join(";", [ for i in 0 .. frames - 1 -> (float i / float frames).ToString("0.####", inv) ]))
                (String.Join(";", values |> Array.map escape))

    let attributes (fixedAttrs: (string * string) list) =
        fixedAttrs
        |> List.map (fun (k, v) -> sprintf " %s=\"%s\"" k (escape v))
        |> String.concat ""

    let element
        (name: string)
        (fixedAttrs: (string * string) list)
        (animate: (string * string[]) list)
        (inner: string)
        =
        let anims = animate |> List.map (fun (a, vs) -> animation a vs) |> String.concat ""

        if anims = "" && inner = "" then
            body.Append(sprintf "<%s%s/>" name (attributes fixedAttrs)).Append('\n')
            |> ignore
        else
            body.Append(sprintf "<%s%s>%s%s</%s>" name (attributes fixedAttrs) (escape inner) anims name).Append '\n'
            |> ignore

    // Where a value is animated, its first frame is the fixed value too, so a
    // viewer that does not animate shows the first frame.
    let withFirst (fixedAttrs: (string * string) list) (animate: (string * string[]) list) =
        let animated = animate |> List.map fst |> Set.ofList

        (fixedAttrs |> List.filter (fun (k, _) -> not (animated.Contains k)))
        @ (animate |> List.map (fun (k, vs) -> (k, vs.[0])))

    let numbers (animate: (string * float[]) list) =
        animate |> List.map (fun (k, vs) -> (k, vs |> Array.map num))

    member _.Width = width
    member _.Height = height
    member _.Frames = frames

    /// Any element, with fixed attributes and per-frame ones.
    member _.Element(name: string, attrs: (string * string) list, ?animate: (string * string[]) list, ?text: string) =
        let animate = defaultArg animate []
        element name (withFirst attrs animate) animate (defaultArg text "")

    member this.Rect
        (
            x: float,
            y: float,
            w: float,
            h: float,
            ?fill: string,
            ?stroke: string,
            ?rx: float,
            ?opacity: float,
            ?animate: (string * float[]) list
        ) =
        this.Element(
            "rect",
            [
                "x", num x
                "y", num y
                "width", num (max 0.0 w)
                "height", num (max 0.0 h)
                "fill", defaultArg fill "none"
                "stroke", defaultArg stroke "none"
                "rx", num (defaultArg rx 0.0)
                "opacity", num (defaultArg opacity 1.0)
            ],
            numbers (defaultArg animate [])
        )

    member this.Circle
        (
            cx: float,
            cy: float,
            r: float,
            ?fill: string,
            ?stroke: string,
            ?width: float,
            ?opacity: float,
            ?animate: (string * float[]) list
        ) =
        this.Element(
            "circle",
            [
                "cx", num cx
                "cy", num cy
                "r", num r
                "fill", defaultArg fill "none"
                "stroke", defaultArg stroke "none"
                "stroke-width", num (defaultArg width 1.0)
                "opacity", num (defaultArg opacity 1.0)
            ],
            numbers (defaultArg animate [])
        )

    member this.Line
        (
            x1: float,
            y1: float,
            x2: float,
            y2: float,
            ?stroke: string,
            ?width: float,
            ?dash: string,
            ?opacity: float,
            ?animate: (string * float[]) list
        ) =
        this.Element(
            "line",
            [
                "x1", num x1
                "y1", num y1
                "x2", num x2
                "y2", num y2
                "stroke", defaultArg stroke ink
                "stroke-width", num (defaultArg width 1.0)
                "stroke-dasharray", defaultArg dash "none"
                "stroke-linecap", "round"
                "opacity", num (defaultArg opacity 1.0)
            ],
            numbers (defaultArg animate [])
        )

    /// A path; `shapes` gives one path per frame (same commands and point
    /// count in each, so it morphs smoothly), else `d` stays.
    member this.Path
        (d: string, ?stroke: string, ?width: float, ?fill: string, ?opacity: float, ?shapes: string[], ?dash: string)
        =
        this.Element(
            "path",
            [
                "d", d
                "stroke", defaultArg stroke ink
                "stroke-width", num (defaultArg width 1.5)
                "fill", defaultArg fill "none"
                "stroke-linejoin", "round"
                "stroke-linecap", "round"
                "stroke-dasharray", defaultArg dash "none"
                "opacity", num (defaultArg opacity 1.0)
            ],
            (match shapes with
             | Some f -> [ ("d", f) ]
             | None -> [])
        )

    /// `transform`, e.g. "rotate(-90 20 200)" for an axis label.
    member this.Text
        (
            x: float,
            y: float,
            s: string,
            ?size: float,
            ?fill: string,
            ?bold: bool,
            ?anchor: string,
            ?opacity: float,
            ?animate: (string * float[]) list,
            ?transform: string
        ) =
        this.Element(
            "text",
            [
                "x", num x
                "y", num y
                "font-size", num (defaultArg size 12.0)
                "fill", defaultArg fill ink
                "font-weight", (if defaultArg bold false then "bold" else "normal")
                "text-anchor", defaultArg anchor "start"
                "font-family", fontFamily
                "opacity", num (defaultArg opacity 1.0)
                yield!
                    (match transform with
                     | Some t -> [ ("transform", t) ]
                     | None -> [])
            ],
            numbers (defaultArg animate []),
            s
        )

    /// Text that changes each frame: one label per frame. A label repeated
    /// over consecutive frames is one element shown for all of them; an empty
    /// label shows nothing.
    member _.FrameText
        (x: float, y: float, labels: string[], ?size: float, ?fill: string, ?bold: bool, ?anchor: string)
        =
        if labels.Length <> frames then
            invalidArg "labels" $"%d{labels.Length} labels for %d{frames} frames"

        let at (k: int) =
            (float k / float frames).ToString("0.####", inv)

        // Runs of one label: (first frame, frame after the last, label).
        let runs =
            labels
            |> Array.indexed
            |> Array.fold
                (fun (acc: (int * int * string) list) (k, label) ->
                    match acc with
                    | (s, _, l) :: rest when l = label -> (s, k + 1, l) :: rest
                    | _ -> (k, k + 1, label) :: acc)
                []
            |> List.rev

        for first, after, label in runs do
            if label <> "" then
                // Shown from `first` up to `after`, hidden the rest of the loop.
                let keys, values =
                    match first = 0, after = frames with
                    | true, true -> ([], [])
                    | true, false -> ([ "0"; at after ], [ "visible"; "hidden" ])
                    | false, true -> ([ "0"; at first ], [ "hidden"; "visible" ])
                    | false, false -> ([ "0"; at first; at after ], [ "hidden"; "visible"; "hidden" ])

                let animation =
                    if keys.IsEmpty then
                        ""
                    else
                        sprintf
                            "<animate attributeName=\"visibility\" dur=\"%ss\" repeatCount=\"indefinite\" calcMode=\"discrete\" keyTimes=\"%s\" values=\"%s\"/>"
                            (num durationS)
                            (String.Join(";", keys))
                            (String.Join(";", values))

                let fixedAttrs =
                    [
                        "x", num x
                        "y", num y
                        "font-size", num (defaultArg size 12.0)
                        "fill", defaultArg fill ink
                        "font-weight", (if defaultArg bold false then "bold" else "normal")
                        "text-anchor", defaultArg anchor "start"
                        "font-family", fontFamily
                        "visibility", (if first = 0 then "visible" else "hidden")
                    ]

                body.Append(sprintf "<text%s>%s%s</text>" (attributes fixedAttrs) (escape label) animation).Append '\n'
                |> ignore

    /// A bar that fills left to right as the frames go by.
    member this.Progress(x: float, y: float, w: float, ?fill: string) =
        this.Rect(x, y, w, 4.0, fill = light, rx = 2.0)

        this.Rect(
            x,
            y,
            0.0,
            4.0,
            fill = defaultArg fill grey,
            rx = 2.0,
            animate = [ ("width", Array.init frames (fun k -> w * float (k + 1) / float frames)) ]
        )

    /// Writes the picture; `quiet` leaves out the "Drawn:" line.
    member _.Save(path: string, ?quiet: bool) =
        let dir = Path.GetDirectoryName(Path.GetFullPath path)
        Directory.CreateDirectory dir |> ignore

        let svg =
            String.concat
                "\n"
                [
                    sprintf
                        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"%s\" height=\"%s\" viewBox=\"0 0 %s %s\" font-family=\"Helvetica, Arial, sans-serif\">"
                        (num width)
                        (num height)
                        (num width)
                        (num height)
                    sprintf "<title>%s</title>" (escape title)
                    sprintf "<rect width=\"%s\" height=\"%s\" fill=\"white\"/>" (num width) (num height)
                    body.ToString().TrimEnd()
                    "</svg>"
                    ""
                ]

        File.WriteAllText(path, svg)

        if not (defaultArg quiet false) then
            printfn "Drawn: %s (animated, %s s loop, %d frames)" path (num durationS) frames

/// `--svg [path]` on the command line: where to draw the picture, if at all.
let svgPath (defaultPath: string) =
    let args = fsi.CommandLineArgs

    match Array.tryFindIndex ((=) "--svg") args with
    | Some i when i + 1 < args.Length && not (args.[i + 1].StartsWith "--") -> Some args.[i + 1]
    | Some _ -> Some defaultPath
    | None -> None
