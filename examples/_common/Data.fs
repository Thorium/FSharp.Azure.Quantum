namespace FSharp.Azure.Quantum.Examples.Common

open System
open System.IO
open System.Security.Cryptography
open System.Text

module Data =
    let ensureDirectory (path: string) =
        Directory.CreateDirectory(path) |> ignore

    let readAllBytes (path: string) =
        File.ReadAllBytes path

    let sha256Hex (bytes: byte array) : string =
        use sha = SHA256.Create()
        let hash = sha.ComputeHash(bytes)
        hash |> Array.map (fun b -> b.ToString("x2")) |> String.concat ""

    let fileSha256Hex (path: string) =
        readAllBytes path |> sha256Hex

    type CsvRow = { Values: Map<string, string> }

    type private CsvSplitState =
        { InQuotes: bool
          FieldRev: char list
          FieldsRev: string list }

    let private splitCsvLine (line: string) : string array =
        // Minimal CSV parser: supports commas and optional quotes (no embedded newlines).
        // Good enough for examples; production should use a real CSV library.

        let flush (s: CsvSplitState) =
            let field = s.FieldRev |> List.rev |> Array.ofList |> fun cs -> String(cs)
            { s with
                FieldRev = []
                FieldsRev = field :: s.FieldsRev }

        let finalState =
            line
            |> Seq.fold
                (fun s c ->
                    match c with
                    | '"' -> { s with InQuotes = not s.InQuotes }
                    | ',' when not s.InQuotes -> flush s
                    | _ -> { s with FieldRev = c :: s.FieldRev })
                { InQuotes = false; FieldRev = []; FieldsRev = [] }
            |> flush

        finalState.FieldsRev
        |> List.rev
        |> List.map (fun x -> x.Trim())
        |> List.toArray

    let readCsvWithHeaderWithErrors (path: string) : CsvRow list * string list =
        let lines =
            File.ReadAllLines path
            |> Array.map (fun l -> l.Trim())
            |> Array.filter (fun l -> not (String.IsNullOrWhiteSpace l) && not (l.StartsWith("#", StringComparison.Ordinal)))

        if lines.Length = 0 then
            ([], [])
        else
            let header = splitCsvLine lines.[0] |> Array.map (fun h -> h.Trim())
            let rows, errors =
                lines
                |> Array.skip 1
                |> Array.toList
                |> List.mapi (fun i line ->
                    let fields = splitCsvLine line
                    if fields.Length <> header.Length then
                        Error (sprintf "row=%d expected %d fields, got %d" (i + 2) header.Length fields.Length)
                    else
                        header
                        |> Array.zip fields
                        |> Array.map (fun (v, k) -> k, v)
                        |> Map.ofArray
                        |> fun m -> Ok { Values = m })
                |> List.fold
                    (fun (oks, errs) r ->
                        match r with
                        | Ok v -> (v :: oks, errs)
                        | Error e -> (oks, e :: errs))
                    ([], [])

            (List.rev rows, List.rev errors)

    let readCsvWithHeader (path: string) : CsvRow list =
        readCsvWithHeaderWithErrors path |> fst
