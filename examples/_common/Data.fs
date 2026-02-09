namespace FSharp.Azure.Quantum.Examples.Common

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json

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

    /// Read a JSON file and deserialize to a list of string-keyed dictionaries.
    /// Useful for pipeline composition where one script's JSON output feeds another.
    let readJsonRows (path: string) : CsvRow list =
        let json = File.ReadAllText path
        let doc = JsonDocument.Parse json
        match doc.RootElement.ValueKind with
        | JsonValueKind.Array ->
            [ for elem in doc.RootElement.EnumerateArray() do
                if elem.ValueKind = JsonValueKind.Object then
                    let values =
                        [ for prop in elem.EnumerateObject() ->
                            prop.Name, prop.Value.ToString() ]
                        |> Map.ofList
                    { Values = values } ]
        | _ -> []

    /// Read lines from a text file, trimming whitespace and skipping blank/comment lines.
    let readLines (path: string) : string list =
        File.ReadAllLines path
        |> Array.map (fun l -> l.Trim())
        |> Array.filter (fun l ->
            not (String.IsNullOrWhiteSpace l)
            && not (l.StartsWith("#", StringComparison.Ordinal)))
        |> Array.toList

    /// Read SMILES strings from a .smi or .csv file (one SMILES per line, or first column).
    let readSmiles (path: string) : string list =
        let ext = Path.GetExtension(path).ToLowerInvariant()
        match ext with
        | ".csv" ->
            let rows = readCsvWithHeader path
            rows
            |> List.choose (fun r ->
                r.Values
                |> Map.tryFind "smiles"
                |> Option.orElse (r.Values |> Map.tryFind "SMILES")
                |> Option.orElse (
                    // Fall back to first column value
                    r.Values |> Map.toList |> List.tryHead |> Option.map snd))
        | _ ->
            // .smi or plain text: one SMILES per line (optionally tab-separated id)
            readLines path
            |> List.map (fun line ->
                match line.Split([| '\t'; ' ' |], 2) with
                | [| smiles; _ |] -> smiles
                | _ -> line)

    /// Resolve a file path relative to the script's directory.
    let resolveRelative (scriptDir: string) (path: string) : string =
        if Path.IsPathRooted path then path
        else Path.Combine(scriptDir, path) |> Path.GetFullPath
