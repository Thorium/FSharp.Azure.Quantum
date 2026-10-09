// Copyright (c) 2025 FSharp.Azure.Quantum Contributors
// SPDX-License-Identifier: MIT

/// The air bridge on a map: the sources and the targets where they are, the corridors the
/// plan has open as lines whose width is the drones on them, the fire's intensity as the
/// targets' colour, the wind, and the clock, frame by frame over the run. Plain SVG with
/// SMIL animation over OpenStreetMap tiles (MapUnderlay), or over a plain ground when the
/// tiles cannot be fetched.
module FSharp.Azure.Quantum.Examples.Drones.FireAirBridge.MapPicture

open System
open System.Globalization
open System.IO
open System.Text
open FSharp.Azure.Quantum.Examples.Drones

/// A source or a target: its id, name and geodetic position.
type Place = { Id: string; Name: string; Lat: float; Lon: float }

/// One corridor as flown in a frame.
type Lane =
    {
        Source: int
        Sector: int
        Drones: float
        Draining: bool
    }

/// The plan at one moment.
type Frame =
    {
        Tick: int
        /// The clock, e.g. "T+15 min"
        Clock: string
        Lanes: Lane[]
        /// The corridors the latest plan was chosen from, as (source, sector)
        Candidates: (int * int)[]
        /// Per target, in [0, 1]: fire intensity, or the share of touches still owed
        Progress: float[]
        /// Sources closed at this moment
        Closed: Set<string>
        WindToDeg: float
        WindMs: float
        /// The demand's headline number and the units delivered so far, as text
        Status: string
        /// What happened since the last frame: events and plan decisions
        Notes: string list
    }

let private inv = CultureInfo.InvariantCulture
let private num (v: float) = v.ToString("0.##", inv)
let private escape (s: string) = System.Security.SecurityElement.Escape s

let private mapWidth, mapHeight = 1000.0, 600.0
let private footer = 110.0

/// The scale the map opens at (one zoom step in from the fetched box) and returns to.
[<Literal>]
let HomeScale = 1.25

/// Writes the picture: `secondsPerFrame` on each frame, looping. `subtitle` names the
/// resources the plan is chosen under. `cacheDir` holds the tiles; a fetch failure is
/// printed and the picture is drawn on a plain ground.
let write
    (path: string)
    (title: string)
    (subtitle: string)
    (cacheDir: string)
    (sources: Place[])
    (sectors: Place[])
    (sectorAreaHa: float[])
    (frames: Frame[])
    (secondsPerFrame: float)
    : unit =
    if frames.Length = 0 then
        invalidArg "frames" "a picture needs at least one frame"

    let n = frames.Length
    let duration = secondsPerFrame * float n
    let places = Array.append sources sectors
    let lats = places |> Array.map (fun p -> p.Lat)
    let lons = places |> Array.map (fun p -> p.Lon)

    // The places' box, widened (or heightened) to the map area's shape so the tiles
    // fill it; the shape is measured in Mercator pixels, where the map is drawn
    let box =
        let minLat, minLon, maxLat, maxLon = Array.min lats, Array.min lons, Array.max lats, Array.max lons
        let x0, y0 = MapUnderlay.pixel 15 (maxLat, minLon)
        let x1, y1 = MapUnderlay.pixel 15 (minLat, maxLon)
        let w, h = max 1.0 (x1 - x0), max 1.0 (y1 - y0)
        let wanted = mapWidth / mapHeight

        if w / h < wanted then
            let grow = (h * wanted - w) / w * (maxLon - minLon) / 2.0
            minLat, minLon - grow, maxLat, maxLon + grow
        else
            let grow = (w / wanted - h) / h * (maxLat - minLat) / 2.0
            minLat - grow, minLon, maxLat + grow, maxLon

    let underlay =
        match MapUnderlay.fetch cacheDir box 0.35 mapWidth mapHeight with
        | Ok u -> Some u
        | Error e ->
            printfn "⚠ map tiles not available (%s): the picture is drawn without the map" e
            None

    // A coarser layer around the detailed one, for zooming out: the same picture budget
    // over a box eight times wider, so it lands at a lower zoom
    let context =
        match underlay with
        | Some detail ->
            match MapUnderlay.fetch cacheDir box 4.0 (mapWidth / 2.0) (mapHeight / 2.0) with
            | Ok c when c.Zoom < detail.Zoom -> Some c
            | _ -> None
        | None -> None

    let image (x: float) (y: float) (size: float) (bytes: byte[]) =
        sprintf
            "<image x=\"%s\" y=\"%s\" width=\"%s\" height=\"%s\" href=\"data:image/png;base64,%s\"/>"
            (num x)
            (num y)
            (num (size + 0.5))
            (num (size + 0.5))
            (Convert.ToBase64String bytes)

    // The projection onto the map area: the underlay's frame scaled to fit, or, without
    // tiles, the box itself with a margin
    let project, tilesSvg, scale =
        match underlay with
        | Some u ->
            let s = min (mapWidth / u.WidthPx) (mapHeight / u.HeightPx)
            let left = (mapWidth - u.WidthPx * s) / 2.0
            let top = (mapHeight - u.HeightPx * s) / 2.0

            let coarse =
                match context with
                | Some c ->
                    let factor = Math.Pow(2.0, float (u.Zoom - c.Zoom))

                    c.Tiles
                    |> List.map (fun (x, y, bytes) ->
                        image
                            (left + ((c.OriginX + x) * factor - u.OriginX) * s)
                            (top + ((c.OriginY + y) * factor - u.OriginY) * s)
                            (256.0 * factor * s)
                            bytes)
                | None -> []

            let tiles =
                u.Tiles |> List.map (fun (x, y, bytes) -> image (left + x * s) (top + y * s) (256.0 * s) bytes)

            (fun (lat, lon) ->
                let x, y = u.Project(lat, lon)
                left + x * s, top + y * s),
            String.concat "\n" (coarse @ tiles),
            s
        | None ->
            let minLat, minLon, maxLat, maxLon = box
            let zoom = 15
            let x0, y0 = MapUnderlay.pixel zoom (maxLat, minLon)
            let x1, y1 = MapUnderlay.pixel zoom (minLat, maxLon)
            let s = min ((mapWidth - 160.0) / max 1.0 (x1 - x0)) ((mapHeight - 160.0) / max 1.0 (y1 - y0))

            (fun point ->
                let x, y = MapUnderlay.pixel zoom point
                80.0 + (x - x0) * s + (mapWidth - 160.0 - (x1 - x0) * s) / 2.0,
                80.0 + (y - y0) * s + (mapHeight - 160.0 - (y1 - y0) * s) / 2.0),
            "",
            s

    let at (p: Place) = project (p.Lat, p.Lon)
    let key (k: int) = (float k / float n).ToString("0.####", inv)
    let frameKeys = String.Join(";", [ for k in 0 .. n - 1 -> key k ])

    let discrete (attribute: string) (values: string seq) =
        sprintf
            "<animate attributeName=\"%s\" dur=\"%ss\" repeatCount=\"indefinite\" calcMode=\"discrete\" keyTimes=\"%s\" values=\"%s\"/>"
            attribute
            (num duration)
            frameKeys
            (String.Join(";", values))

    /// An element shown during frame k only
    let shownIn (k: int) =
        if n = 1 then
            ""
        else
            let keys, values =
                if k = 0 then sprintf "0;%s" (key 1), "visible;hidden"
                elif k = n - 1 then sprintf "0;%s" (key k), "hidden;visible"
                else sprintf "0;%s;%s" (key k) (key (k + 1)), "hidden;visible;hidden"

            sprintf
                "<animate attributeName=\"visibility\" dur=\"%ss\" repeatCount=\"indefinite\" calcMode=\"discrete\" keyTimes=\"%s\" values=\"%s\"/>"
                (num duration)
                keys
                values

    let visibility (k: int) = if k = 0 then "visible" else "hidden"

    let svg = StringBuilder()
    let line (s: string) = svg.Append(s).Append('\n') |> ignore
    let height = mapHeight + footer

    line (
        sprintf
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"%s\" height=\"%s\" viewBox=\"0 0 %s %s\" font-family=\"Helvetica, Arial, sans-serif\">"
            (num mapWidth)
            (num height)
            (num mapWidth)
            (num height)
    )

    line (sprintf "<title>%s</title>" (escape title))
    line (sprintf "<rect width=\"%s\" height=\"%s\" fill=\"#e8efe6\"/>" (num mapWidth) (num height))
    line "<clipPath id=\"map\"><rect x=\"0\" y=\"0\" width=\"1000\" height=\"600\"/></clipPath>"
    // The map area pans and zooms as one (see the script at the end); the texts outside
    // it stay put
    line "<g id=\"map-area\" clip-path=\"url(#map)\" style=\"cursor: grab\">"
    line "<rect x=\"0\" y=\"0\" width=\"1000\" height=\"600\" fill=\"#dbe7d6\"/>"

    // Opens one zoom step in, about the centre; the home button comes back here
    line (
        sprintf
            "<g id=\"view\" transform=\"translate(%s %s) scale(%s)\">"
            (num (mapWidth / 2.0 * (1.0 - HomeScale)))
            (num (mapHeight / 2.0 * (1.0 - HomeScale)))
            (num HomeScale)
    )

    line tilesSvg

    // The choice: every corridor that is ever a candidate, as a faint dashed line while it
    // is one. These are the qubits of a round; the plan picks a subset of them.
    let candidatePairs = frames |> Array.collect (fun f -> f.Candidates) |> Array.distinct

    for source, sector in candidatePairs do
        let x1, y1 = at sources.[source]
        let x2, y2 = at sectors.[sector]

        let shown =
            frames
            |> Array.map (fun f ->
                if f.Candidates |> Array.contains (source, sector) then "0.9" else "0")

        line (
            sprintf
                "<line x1=\"%s\" y1=\"%s\" x2=\"%s\" y2=\"%s\" stroke=\"#1d4ed8\" stroke-width=\"2.5\" stroke-linecap=\"round\" stroke-dasharray=\"1 7\" stroke-opacity=\"%s\">%s</line>"
                (num x1)
                (num y1)
                (num x2)
                (num y2)
                shown.[0]
                (discrete "stroke-opacity" shown)
        )

    // Corridors: every source-target pair that ever flies, as one line
    let pairs =
        frames
        |> Array.collect (fun f -> f.Lanes |> Array.map (fun l -> l.Source, l.Sector))
        |> Array.distinct

    for source, sector in pairs do
        let x1, y1 = at sources.[source]
        let x2, y2 = at sectors.[sector]

        let laneIn (f: Frame) =
            f.Lanes |> Array.tryFind (fun l -> l.Source = source && l.Sector = sector)

        let widthValues =
            frames
            |> Array.map (fun f ->
                match laneIn f with
                | Some l -> 2.0 + 0.6 * l.Drones
                | None -> 0.0)

        let widths = widthValues |> Array.map num
        let casings = widthValues |> Array.map (fun w -> num (if w > 0.0 then w + 4.0 else 0.0))

        let dashes =
            frames
            |> Array.map (fun f ->
                match laneIn f with
                | Some l when l.Draining -> "8 6"
                | _ -> "none")

        // A pale casing, then the lane
        line (
            sprintf
                "<line x1=\"%s\" y1=\"%s\" x2=\"%s\" y2=\"%s\" stroke=\"white\" stroke-opacity=\"0.7\" stroke-linecap=\"round\" stroke-width=\"%s\">%s</line>"
                (num x1)
                (num y1)
                (num x2)
                (num y2)
                casings.[0]
                (discrete "stroke-width" casings)
        )

        line (
            sprintf
                "<line x1=\"%s\" y1=\"%s\" x2=\"%s\" y2=\"%s\" stroke=\"#0369a1\" stroke-linecap=\"round\" stroke-width=\"%s\" stroke-dasharray=\"%s\">%s%s</line>"
                (num x1)
                (num y1)
                (num x2)
                (num y2)
                widths.[0]
                dashes.[0]
                (discrete "stroke-width" widths)
                (discrete "stroke-dasharray" dashes)
        )

        // Drones on the corridor: dots flowing from the source to the target while it is
        // open, as many as the lane carries in tens of drones (at least one)
        let flowing =
            frames
            |> Array.map (fun f ->
                match laneIn f with
                | Some l -> max 1 (int (Math.Round(l.Drones / 10.0)))
                | None -> 0)

        let most = Array.max flowing

        for dot in 0 .. most - 1 do
            let visible = flowing |> Array.map (fun count -> if dot < count then "visible" else "hidden")

            line (
                sprintf
                    "<circle r=\"3.5\" fill=\"white\" stroke=\"#0369a1\" stroke-width=\"1.5\" visibility=\"%s\"><animateMotion dur=\"2.4s\" begin=\"%ss\" repeatCount=\"indefinite\" path=\"M %s %s L %s %s\"/>%s</circle>"
                    visible.[0]
                    (num (2.4 * float dot / float most))
                    (num x1)
                    (num y1)
                    (num x2)
                    (num y2)
                    (discrete "visibility" visible)
            )

    // Sources: blue, grey while closed
    sources
    |> Array.iteri (fun i p ->
        let x, y = at p

        let fills =
            frames |> Array.map (fun f -> if f.Closed.Contains p.Id then "#9ca3af" else "#0ea5e9")

        line (
            sprintf
                "<circle cx=\"%s\" cy=\"%s\" r=\"9\" fill=\"%s\" stroke=\"white\" stroke-width=\"2\">%s</circle>"
                (num x)
                (num y)
                fills.[0]
                (discrete "fill" fills)
        )

        line (
            sprintf
                "<text x=\"%s\" y=\"%s\" font-size=\"13\" font-weight=\"bold\" fill=\"#0c4a6e\" stroke=\"white\" stroke-width=\"3\" paint-order=\"stroke\">%s</text>"
                (num (x + 13.0))
                (num (y + 5.0))
                (escape p.Name)
        )

        ignore i)

    // Targets: a disc sized by area, red by intensity and growing with it, with a glow
    // while it burns; outlined when out
    sectors
    |> Array.iteri (fun j p ->
        let x, y = at p
        let footprint = max 10.0 (sqrt (sectorAreaHa.[j] * 10000.0 / Math.PI) * scale * 0.3)
        let intensity (f: Frame) = Math.Clamp(f.Progress.[j], 0.0, 1.0)
        let radii = frames |> Array.map (fun f -> num (footprint * (0.7 + 0.6 * intensity f)))
        let glows = frames |> Array.map (fun f -> num (footprint * (1.0 + 1.2 * intensity f)))
        let glowOpacities = frames |> Array.map (fun f -> num (0.45 * intensity f))
        let opacities = frames |> Array.map (fun f -> num (0.12 + 0.78 * intensity f))

        line (
            sprintf
                "<circle cx=\"%s\" cy=\"%s\" r=\"%s\" fill=\"#f97316\" fill-opacity=\"%s\">%s%s</circle>"
                (num x)
                (num y)
                glows.[0]
                glowOpacities.[0]
                (discrete "r" glows)
                (discrete "fill-opacity" glowOpacities)
        )

        line (
            sprintf
                "<circle cx=\"%s\" cy=\"%s\" r=\"%s\" fill=\"#dc2626\" fill-opacity=\"%s\" stroke=\"#7f1d1d\" stroke-width=\"2\">%s%s</circle>"
                (num x)
                (num y)
                radii.[0]
                opacities.[0]
                (discrete "r" radii)
                (discrete "fill-opacity" opacities)
        )

        line (
            sprintf
                "<text x=\"%s\" y=\"%s\" font-size=\"13\" font-weight=\"bold\" fill=\"#7f1d1d\" stroke=\"white\" stroke-width=\"3\" paint-order=\"stroke\" text-anchor=\"middle\">%s</text>"
                (num x)
                (num (y - footprint * 1.3 - 6.0))
                (escape p.Name)
        ))

    line "</g>"
    line "</g>"

    // Zoom buttons; they work where the script runs (the file opened in a browser)
    for id, label, y in [ "zoom-in", "+", 60.0; "zoom-out", "−", 88.0; "zoom-reset", "⌂", 116.0 ] do
        line (
            sprintf
                "<g id=\"%s\" style=\"cursor: pointer\"><rect x=\"958\" y=\"%s\" width=\"26\" height=\"26\" rx=\"5\" fill=\"white\" fill-opacity=\"0.9\" stroke=\"#9ca3af\"/><text x=\"971\" y=\"%s\" font-size=\"18\" text-anchor=\"middle\" fill=\"#111827\">%s</text></g>"
                id
                (num y)
                (num (y + 19.0))
                label
        )

    // Title, subtitle, clock, attribution
    line (
        sprintf
            "<text x=\"14\" y=\"26\" font-size=\"18\" font-weight=\"bold\" fill=\"#111827\" stroke=\"white\" stroke-width=\"4\" paint-order=\"stroke\">%s</text>"
            (escape title)
    )

    line (
        sprintf
            "<text x=\"14\" y=\"46\" font-size=\"13\" fill=\"#1f2937\" stroke=\"white\" stroke-width=\"3\" paint-order=\"stroke\">%s</text>"
            (escape subtitle)
    )

    for k in 0 .. n - 1 do
        line (
            sprintf
                "<text x=\"986\" y=\"28\" font-size=\"22\" font-weight=\"bold\" text-anchor=\"end\" fill=\"#111827\" stroke=\"white\" stroke-width=\"4\" paint-order=\"stroke\" visibility=\"%s\">%s%s</text>"
                (visibility k)
                (escape frames.[k].Clock)
                (shownIn k)
        )

    line (
        sprintf
            "<text x=\"986\" y=\"592\" font-size=\"11\" text-anchor=\"end\" fill=\"#374151\" stroke=\"white\" stroke-width=\"3\" paint-order=\"stroke\">%s</text>"
            (escape MapUnderlay.Attribution)
    )

    // Wind: an arrow turned the way the wind blows, per frame
    line "<g transform=\"translate(60 540)\">"
    line "<circle r=\"26\" fill=\"white\" fill-opacity=\"0.85\" stroke=\"#9ca3af\"/>"

    line (
        sprintf
            "<g transform=\"rotate(%s)\"><path d=\"M 0 16 L 0 -16 M -7 -8 L 0 -16 L 7 -8\" stroke=\"#111827\" stroke-width=\"3\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/><animateTransform attributeName=\"transform\" type=\"rotate\" dur=\"%ss\" repeatCount=\"indefinite\" calcMode=\"discrete\" keyTimes=\"%s\" values=\"%s\"/></g>"
            (num frames.[0].WindToDeg)
            (num duration)
            frameKeys
            (String.Join(";", frames |> Array.map (fun f -> num f.WindToDeg)))
    )

    line "</g>"

    for k in 0 .. n - 1 do
        line (
            sprintf
                "<text x=\"96\" y=\"545\" font-size=\"13\" fill=\"#111827\" stroke=\"white\" stroke-width=\"3\" paint-order=\"stroke\" visibility=\"%s\">wind %s m/s toward %s°%s</text>"
                (visibility k)
                (num frames.[k].WindMs)
                (num frames.[k].WindToDeg)
                (shownIn k)
        )

    // Footer: status and the notes of the frame, and a legend
    line (sprintf "<rect x=\"0\" y=\"%s\" width=\"1000\" height=\"%s\" fill=\"#111827\"/>" (num mapHeight) (num footer))

    for k in 0 .. n - 1 do
        let f = frames.[k]

        line (
            sprintf
                "<text x=\"14\" y=\"%s\" font-size=\"15\" font-weight=\"bold\" fill=\"#f9fafb\" visibility=\"%s\">%s%s</text>"
                (num (mapHeight + 26.0))
                (visibility k)
                (escape f.Status)
                (shownIn k)
        )

        f.Notes
        |> List.truncate 3
        |> List.iteri (fun i note ->
            line (
                sprintf
                    "<text x=\"14\" y=\"%s\" font-size=\"13\" fill=\"#d1d5db\" visibility=\"%s\">%s%s</text>"
                    (num (mapHeight + 48.0 + 18.0 * float i))
                    (visibility k)
                    (escape note)
                    (shownIn k)
            ))

    line (
        sprintf
            "<text x=\"986\" y=\"%s\" font-size=\"12\" text-anchor=\"end\" fill=\"#9ca3af\">dotted: a candidate corridor (one qubit) · solid: open, width and dots the drones on it · dashed: draining · red: fire intensity · grey source: closed</text>"
            (num (mapHeight + 96.0))
    )

    // Wheel zoom about the pointer, drag to pan, the buttons; between 1/4 and 6 times
    line (sprintf "<script><![CDATA[var HOME = %s;" (num HomeScale))

    line
        """(function () {
  var svg = document.documentElement, view = document.getElementById('view'), area = document.getElementById('map-area');
  if (!view || !area) return;
  var W = 1000, H = 600;
  var s = HOME, tx = W / 2 * (1 - HOME), ty = H / 2 * (1 - HOME);
  function apply() { view.setAttribute('transform', 'translate(' + tx + ' ' + ty + ') scale(' + s + ')'); }
  function zoomAt(f, px, py) {
    var ns = Math.min(6, Math.max(0.25, s * f)); f = ns / s;
    tx = px - f * (px - tx); ty = py - f * (py - ty); s = ns; apply();
  }
  function pt(e) {
    var p = svg.createSVGPoint(); p.x = e.clientX; p.y = e.clientY;
    var m = svg.getScreenCTM(); return m ? p.matrixTransform(m.inverse()) : p;
  }
  area.addEventListener('wheel', function (e) {
    var p = pt(e); if (p.y > H) return;
    e.preventDefault(); zoomAt(e.deltaY < 0 ? 1.2 : 1 / 1.2, p.x, p.y);
  }, { passive: false });
  var drag = null;
  area.addEventListener('mousedown', function (e) { var p = pt(e); if (p.y > H) return; drag = { x: p.x, y: p.y, tx: tx, ty: ty }; e.preventDefault(); });
  window.addEventListener('mousemove', function (e) { if (!drag) return; var p = pt(e); tx = drag.tx + (p.x - drag.x); ty = drag.ty + (p.y - drag.y); apply(); });
  window.addEventListener('mouseup', function () { drag = null; });
  function button(id, f) { var el = document.getElementById(id); if (el) el.addEventListener('click', f); }
  button('zoom-in', function () { zoomAt(1.25, W / 2, H / 2); });
  button('zoom-out', function () { zoomAt(1 / 1.25, W / 2, H / 2); });
  button('zoom-reset', function () { s = HOME; tx = W / 2 * (1 - HOME); ty = H / 2 * (1 - HOME); apply(); });
})();
]]></script>"""

    line "</svg>"
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath path)) |> ignore
    File.WriteAllText(path, svg.ToString())

    printfn
        "Drawn: %s (animated, %s s loop, %d frames%s; wheel, drag and the buttons zoom and pan it when opened in a browser)"
        path
        (num duration)
        n
        (if underlay.IsSome then ", OpenStreetMap" else ", no map")
