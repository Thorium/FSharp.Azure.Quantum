namespace FSharp.Azure.Quantum.Examples.Common

open System

module Cli =
    type ParsedArgs =
        { Flags: Set<string>
          Values: Map<string, string list> }

    let parse (argv: string array) : ParsedArgs =
        let rec loop i (flags: Set<string>) (values: Map<string, string list>) =
            if i >= argv.Length then
                { Flags = flags; Values = values }
            else
                let token = argv.[i]
                if token.StartsWith("--", StringComparison.Ordinal) then
                    let key = token.Substring(2)
                    let nextIsValue =
                        i + 1 < argv.Length && not (argv.[i + 1].StartsWith("--", StringComparison.Ordinal))

                    if nextIsValue then
                        let v = argv.[i + 1]
                        let updated =
                            values
                            |> Map.change key (fun existing ->
                                match existing with
                                | None -> Some [ v ]
                                | Some xs -> Some (xs @ [ v ]))

                        loop (i + 2) flags updated
                    else
                        loop (i + 1) (flags.Add key) values
                else
                    // Ignore bare tokens (treating this CLI as flag-only).
                    loop (i + 1) flags values

        loop 0 Set.empty Map.empty

    let hasFlag (name: string) (args: ParsedArgs) =
        args.Flags.Contains name

    let tryGet (name: string) (args: ParsedArgs) : string option =
        args.Values |> Map.tryFind name |> Option.bind List.tryLast

    let getOr (name: string) (fallback: string) (args: ParsedArgs) : string =
        tryGet name args |> Option.defaultValue fallback

    let getIntOr (name: string) (fallback: int) (args: ParsedArgs) : int =
        match tryGet name args with
        | None -> fallback
        | Some s ->
            match Int32.TryParse s with
            | true, v -> v
            | false, _ -> fallback

    let getFloatOr (name: string) (fallback: float) (args: ParsedArgs) : float =
        match tryGet name args with
        | None -> fallback
        | Some s ->
            match Double.TryParse s with
            | true, v -> v
            | false, _ -> fallback

    let getList (name: string) (args: ParsedArgs) : string list =
        args.Values |> Map.tryFind name |> Option.defaultValue []

    /// Split a comma-separated value into a list of trimmed strings.
    let getCommaSeparated (name: string) (args: ParsedArgs) : string list =
        match tryGet name args with
        | None -> []
        | Some s -> s.Split(',') |> Array.map (fun x -> x.Trim()) |> Array.toList

    type OptionSpec =
        { Name: string
          Description: string
          Default: string option }

    /// Print a usage banner and exit if --help is present.
    let exitIfHelp (scriptName: string) (description: string) (options: OptionSpec list) (args: ParsedArgs) =
        if hasFlag "help" args then
            printfn ""
            printfn "  %s" scriptName
            printfn "  %s" description
            printfn ""
            printfn "  Usage: dotnet fsi %s -- [OPTIONS]" scriptName
            printfn ""
            printfn "  Options:"
            for opt in options do
                let defaultStr =
                    match opt.Default with
                    | Some d -> sprintf " (default: %s)" d
                    | None -> ""
                printfn "    --%-20s %s%s" opt.Name opt.Description defaultStr
            printfn "    --%-20s %s" "help" "Show this help message"
            printfn ""
            printfn "  Examples:"
            printfn "    dotnet fsi %s" scriptName
            printfn "    dotnet fsi %s -- --help" scriptName
            printfn ""
            exit 0
