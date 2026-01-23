namespace FSharp.Azure.Quantum.Examples.Common

open System
open System.IO
open System.Text
open System.Text.Json

module Reporting =
    let private utf8NoBom = UTF8Encoding(false)

    let writeTextFile (path: string) (content: string) =
        let dir = Path.GetDirectoryName path
        if not (String.IsNullOrWhiteSpace dir) then
            Directory.CreateDirectory(dir) |> ignore
        File.WriteAllText(path, content, utf8NoBom)

    let writeJson<'T> (path: string) (value: 'T) =
        let options = JsonSerializerOptions(WriteIndented = true)
        let json = JsonSerializer.Serialize(value, options)
        writeTextFile path (json + "\n")

    let writeCsv (path: string) (header: string list) (rows: string list list) =
        let escape (s: string) =
            if s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r') then
                "\"" + s.Replace("\"", "\"\"") + "\""
            else
                s

        let line (cells: string list) =
            cells |> List.map escape |> String.concat ","

        let sb = StringBuilder()
        sb.AppendLine(line header) |> ignore
        for r in rows do
            sb.AppendLine(line r) |> ignore

        writeTextFile path (sb.ToString())
