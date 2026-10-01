namespace FSharp.Azure.Quantum.Examples.SupplyChain.NetworkFlowOptimization

open System
open System.Globalization
open System.IO
open System.Text

/// Animated SVG for this project: the part of examples/_common/SvgAnimation.fs
/// it draws with (a compiled project cannot load that script), in the same
/// look, plus dotted lines whose dots run continuously along them. A picture
/// is a fixed page whose elements show, hide or resize over `frames` steps;
/// the steps play in `durationS` seconds and loop.
module Svg =
    let private inv = CultureInfo.InvariantCulture

    /// A number as SVG wants it: at most two decimals.
    let num (v: float) = v.ToString("0.##", inv)

    let private escape (s: string) =
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")

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

    let colour (i: int) = palette.[i % palette.Length]

    let ink, grey, light, panel, frameColour =
        "#1a1a1a", "#555555", "#e5e5e5", "#fafafa", "#cccccc"

    [<Literal>]
    let fontFamily = "Helvetica, Arial, sans-serif"

    /// Blend `hex` toward white by `f` (0 = the colour, 1 = white).
    let tint (f: float) (hex: string) =
        let c (i: int) =
            Convert.ToInt32(hex.Substring(i, 2), 16)

        let mix v = int (float v + (255.0 - float v) * f)
        sprintf "#%02x%02x%02x" (mix (c 1)) (mix (c 3)) (mix (c 5))

    type Picture(width: float, height: float, frames: int, durationS: float, title: string) =
        let body = StringBuilder()
        let dur = num durationS + "s"

        let at (k: int) =
            (float k / float frames).ToString("0.####", inv)

        // Shown in the frames where `shown` is true: a key only where it changes.
        let visibility (shown: bool[] option) =
            match shown with
            | None -> ([], "")
            | Some s when s.Length <> frames -> invalidArg "shown" $"%d{s.Length} values for %d{frames} frames"
            | Some s ->
                let word k = if s.[k] then "visible" else "hidden"

                let changes =
                    [
                        for k in 0 .. frames - 1 do
                            if k = 0 || s.[k] <> s.[k - 1] then
                                k
                    ]

                let animation =
                    if changes.Length = 1 then
                        ""
                    else
                        sprintf
                            "<animate attributeName=\"visibility\" dur=\"%s\" repeatCount=\"indefinite\" calcMode=\"discrete\" keyTimes=\"%s\" values=\"%s\"/>"
                            dur
                            (changes |> List.map at |> String.concat ";")
                            (changes |> List.map word |> String.concat ";")

                ([ ("visibility", word 0) ], animation)

        // One number per frame: held for `hold` of the frame, then moving to the next.
        let numeric (attribute: string) (values: float[]) (hold: float) =
            if values.Length <> frames then
                invalidArg attribute $"%d{values.Length} values for %d{frames} frames"

            let keys =
                [
                    for i in 0 .. frames - 1 do
                        yield (float i / float frames, values.[i])
                        yield ((float i + hold) / float frames, values.[i])
                    yield (1.0, values.[frames - 1])
                ]

            sprintf
                "<animate attributeName=\"%s\" dur=\"%s\" repeatCount=\"indefinite\" calcMode=\"linear\" keyTimes=\"%s\" values=\"%s\"/>"
                attribute
                dur
                (keys |> List.map (fun (t, _) -> t.ToString("0.####", inv)) |> String.concat ";")
                (keys |> List.map (snd >> num) |> String.concat ";")

        let element
            (name: string)
            (attrs: (string * string) list)
            (shown: bool[] option)
            (animations: string list)
            (text: string)
            =
            let visible, visibilityAnimation = visibility shown

            let attributes =
                attrs @ visible
                |> List.map (fun (k, v) -> sprintf " %s=\"%s\"" k (escape v))
                |> String.concat ""

            let inner = escape text + visibilityAnimation + String.concat "" animations

            if inner = "" then
                body.Append($"<%s{name}%s{attributes}/>").Append('\n') |> ignore
            else
                body.Append($"<%s{name}%s{attributes}>%s{inner}</%s{name}>").Append('\n')
                |> ignore

        member _.Frames = frames

        member _.Rect
            (
                x: float,
                y: float,
                w: float,
                h: float,
                ?fill: string,
                ?stroke: string,
                ?rx: float,
                ?strokeWidth: float,
                ?dash: string,
                ?shown: bool[],
                ?widths: float[]
            ) =
            let w0 =
                match widths with
                | Some ws -> ws.[0]
                | None -> w

            element
                "rect"
                [
                    "x", num x
                    "y", num y
                    "width", num (max 0.0 w0)
                    "height", num h
                    "rx", num (defaultArg rx 0.0)
                    "fill", defaultArg fill "none"
                    "stroke", defaultArg stroke "none"
                    "stroke-width", num (defaultArg strokeWidth 1.0)
                    "stroke-dasharray", defaultArg dash "none"
                ]
                shown
                (match widths with
                 | Some ws -> [ numeric "width" ws 0.1 ]
                 | None -> [])
                ""

        member _.Circle
            (
                cx: float,
                cy: float,
                r: float,
                ?fill: string,
                ?stroke: string,
                ?strokeWidth: float,
                ?dash: string,
                ?shown: bool[]
            ) =
            element
                "circle"
                [
                    "cx", num cx
                    "cy", num cy
                    "r", num r
                    "fill", defaultArg fill "none"
                    "stroke", defaultArg stroke "none"
                    "stroke-width", num (defaultArg strokeWidth 1.0)
                    "stroke-dasharray", defaultArg dash "none"
                ]
                shown
                []
                ""

        member _.Line(x1: float, y1: float, x2: float, y2: float, ?stroke: string, ?width: float, ?shown: bool[]) =
            element
                "line"
                [
                    "x1", num x1
                    "y1", num y1
                    "x2", num x2
                    "y2", num y2
                    "stroke", defaultArg stroke ink
                    "stroke-width", num (defaultArg width 1.0)
                    "stroke-linecap", "round"
                ]
                shown
                []
                ""

        /// Dots `gap` apart running from (x1, y1) to (x2, y2) at `speed` px/s.
        member _.Dots
            (
                x1: float,
                y1: float,
                x2: float,
                y2: float,
                stroke: string,
                size: float,
                gap: float,
                speed: float,
                ?shown: bool[]
            ) =
            element
                "line"
                [
                    "x1", num x1
                    "y1", num y1
                    "x2", num x2
                    "y2", num y2
                    "stroke", stroke
                    "stroke-width", num size
                    "stroke-linecap", "round"
                    "stroke-dasharray", sprintf "0.01 %s" (num gap)
                ]
                shown
                [
                    sprintf
                        "<animate attributeName=\"stroke-dashoffset\" dur=\"%ss\" repeatCount=\"indefinite\" calcMode=\"linear\" keyTimes=\"0;1\" values=\"%s;0\"/>"
                        (num (gap / speed))
                        (num gap)
                ]
                ""

        /// `halo` draws a white outline behind the letters, to read over lines.
        member _.Text
            (
                x: float,
                y: float,
                s: string,
                ?size: float,
                ?fill: string,
                ?bold: bool,
                ?anchor: string,
                ?shown: bool[],
                ?halo: bool
            ) =
            element
                "text"
                [
                    "x", num x
                    "y", num y
                    "font-size", num (defaultArg size 12.0)
                    "fill", defaultArg fill ink
                    "font-weight", (if defaultArg bold false then "bold" else "normal")
                    "text-anchor", defaultArg anchor "start"
                    "font-family", fontFamily
                    if defaultArg halo false then
                        yield!
                            [
                                ("stroke", "white")
                                ("stroke-width", "3")
                                ("stroke-linejoin", "round")
                                ("paint-order", "stroke")
                            ]
                ]
                shown
                []
                s

        /// One label per frame; a label repeated over consecutive frames is one
        /// element, an empty one shows nothing.
        member this.FrameText
            (x: float, y: float, labels: string[], ?size: float, ?fill: string, ?bold: bool, ?anchor: string)
            =
            if labels.Length <> frames then
                invalidArg "labels" $"%d{labels.Length} labels for %d{frames} frames"

            let starts =
                [
                    for k in 0 .. frames - 1 do
                        if k = 0 || labels.[k] <> labels.[k - 1] then
                            k
                ]

            for first, after in List.pairwise (starts @ [ frames ]) do
                if labels.[first] <> "" then
                    let shown = Array.init frames (fun k -> k >= first && k < after)

                    this.Text(
                        x,
                        y,
                        labels.[first],
                        ?size = size,
                        ?fill = fill,
                        ?bold = bold,
                        ?anchor = anchor,
                        shown = shown
                    )

        /// A bar that fills left to right as the frames go by.
        member this.Progress(x: float, y: float, w: float) =
            this.Rect(x, y, w, 4.0, fill = light, rx = 2.0)

            this.Rect(
                x,
                y,
                0.0,
                4.0,
                fill = grey,
                rx = 2.0,
                widths = Array.init frames (fun k -> w * float (k + 1) / float frames)
            )

        member _.Save(path: string) =
            let full = Path.GetFullPath path
            Directory.CreateDirectory(Path.GetDirectoryName full) |> ignore

            let svg =
                String.concat
                    "\n"
                    [
                        sprintf
                            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"%s\" height=\"%s\" viewBox=\"0 0 %s %s\" font-family=\"%s\">"
                            (num width)
                            (num height)
                            (num width)
                            (num height)
                            fontFamily
                        sprintf "<title>%s</title>" (escape title)
                        sprintf "<rect width=\"%s\" height=\"%s\" fill=\"white\"/>" (num width) (num height)
                        body.ToString().TrimEnd()
                        "</svg>"
                        ""
                    ]

            File.WriteAllText(full, svg)
            printfn "Drawn: %s (animated, %s s loop, %d frames)" full (num durationS) frames
