// Copyright (c) 2025 FSharp.Azure.Quantum Contributors
// SPDX-License-Identifier: MIT

/// A map under a drone picture: OpenStreetMap raster tiles covering a latitude/longitude
/// box, fetched once into a cache directory and placed by the Web Mercator projection, so
/// a plan drawn in local kilometres lands on real streets and shores. The tiles are
/// © OpenStreetMap contributors (ODbL); the picture must show that line, and the fetch
/// identifies itself as the tile usage policy asks. A box of a few kilometres at the zoom
/// chosen here is twenty to forty tiles, well within the policy for a picture made once.
///
/// Google and Bing imagery is not used: their terms do not allow a static copy to be
/// published in a repository or on a page.
module FSharp.Azure.Quantum.Examples.Drones.MapUnderlay

open System
open System.IO
open System.Net.Http

/// Where the tiles come from and the credit the picture owes.
[<Literal>]
let Attribution = "© OpenStreetMap contributors"

[<Literal>]
let private TileUrl = "https://tile.openstreetmap.org/{0}/{1}/{2}.png"

[<Literal>]
let private UserAgent = "FSharp.Azure.Quantum-examples/1.0 (+https://github.com/thorium/fsharp.azure.quantum)"

/// Pixels along a side of the world at `zoom` (256-pixel tiles).
let private worldPx (zoom: int) = 256.0 * Math.Pow(2.0, float zoom)

/// Web Mercator pixel of a point at `zoom`, measured from the world's top-left.
let pixel (zoom: int) (lat: float, lon: float) : float * float =
    let world = worldPx zoom
    let x = (lon + 180.0) / 360.0 * world
    let latR = Math.Clamp(lat, -85.0511, 85.0511) * Math.PI / 180.0
    let y = (1.0 - Math.Log(Math.Tan latR + 1.0 / Math.Cos latR) / Math.PI) / 2.0 * world
    x, y

/// Tiles covering a box, their pixel frame, and the projection into it.
type Underlay =
    {
        Zoom: int
        /// Pixel size of the covered area (whole tiles)
        WidthPx: float
        HeightPx: float
        /// (column, row, PNG bytes) for each tile, with its top-left in the frame
        Tiles: (float * float * byte[]) list
        /// The frame's top-left in the world's pixels at Zoom, so a second underlay at
        /// another zoom can be laid under this one: a world pixel at zoom z is 2^(Zoom − z)
        /// of these
        OriginX: float
        OriginY: float
        /// A point's pixel within the frame (top-left of the first tile = 0, 0)
        Project: float * float -> float * float
    }

/// The largest zoom (at most 18) at which the box is at most `widthPx` wide and
/// `heightPx` tall, so the map shows as much detail as the picture can carry.
let private zoomFor (minLat: float, minLon: float, maxLat: float, maxLon: float) (widthPx: float) (heightPx: float) =
    [ 18 .. -1 .. 1 ]
    |> List.tryFind (fun z ->
        let x0, y0 = pixel z (maxLat, minLon)
        let x1, y1 = pixel z (minLat, maxLon)
        x1 - x0 <= widthPx && y1 - y0 <= heightPx)
    |> Option.defaultValue 1

/// The tile at (zoom, column, row): from `cacheDir`, else fetched and cached. An Error
/// names the failure (no network, a refusal) so the caller can draw without the map.
let private tile (client: HttpClient) (cacheDir: string) (zoom: int) (column: int) (row: int) : Result<byte[], string> =
    let path = Path.Combine(cacheDir, string zoom, string column, string row + ".png")

    if File.Exists path then
        Ok(File.ReadAllBytes path)
    else
        try
            let bytes =
                client.GetByteArrayAsync(String.Format(TileUrl, zoom, column, row)).GetAwaiter().GetResult()

            Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
            File.WriteAllBytes(path, bytes)
            Ok bytes
        with ex ->
            Error $"tile %d{zoom}/%d{column}/%d{row}: %s{ex.Message}"

/// The underlay for a box (degrees), grown by `marginShare` on every side, at the zoom
/// that fits `widthPx` × `heightPx`. Tiles are cached under `cacheDir`.
let fetch
    (cacheDir: string)
    (minLat: float, minLon: float, maxLat: float, maxLon: float)
    (marginShare: float)
    (widthPx: float)
    (heightPx: float)
    : Result<Underlay, string> =
    let dLat = max 0.002 (maxLat - minLat) * marginShare
    let dLon = max 0.002 (maxLon - minLon) * marginShare
    let box = minLat - dLat, minLon - dLon, maxLat + dLat, maxLon + dLon
    let bMinLat, bMinLon, bMaxLat, bMaxLon = box
    let zoom = zoomFor box widthPx heightPx
    let x0, y0 = pixel zoom (bMaxLat, bMinLon)
    let x1, y1 = pixel zoom (bMinLat, bMaxLon)
    let column0, row0 = int (floor (x0 / 256.0)), int (floor (y0 / 256.0))
    let column1, row1 = int (floor (x1 / 256.0)), int (floor (y1 / 256.0))
    let originX, originY = float column0 * 256.0, float row0 * 256.0

    use client = new HttpClient()
    client.DefaultRequestHeaders.UserAgent.ParseAdd UserAgent
    client.Timeout <- TimeSpan.FromSeconds 20.0

    let tiles =
        [
            for row in row0..row1 do
                for column in column0..column1 do
                    tile client cacheDir zoom column row
                    |> Result.map (fun bytes -> float column * 256.0 - originX, float row * 256.0 - originY, bytes)
        ]

    match tiles |> List.tryPick (function Error e -> Some e | Ok _ -> None) with
    | Some failure -> Error failure
    | None ->
        Ok
            {
                Zoom = zoom
                WidthPx = float (column1 - column0 + 1) * 256.0
                HeightPx = float (row1 - row0 + 1) * 256.0
                Tiles = tiles |> List.choose (function Ok t -> Some t | Error _ -> None)
                OriginX = originX
                OriginY = originY
                Project =
                    fun point ->
                        let x, y = pixel zoom point
                        x - originX, y - originY
            }
