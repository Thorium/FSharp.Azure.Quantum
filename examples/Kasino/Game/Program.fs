namespace FSharp.Azure.Quantum.Examples.Kasino

open System
open Spectre.Console
open FSharp.Azure.Quantum.Backends

/// Main entry point for Kasino card game
module Program =

    open GameLoop

    /// Parsed command-line options
    type private CliOptions =
        { Variant: GameVariant option
          Players: int option
          Humans: int option
          Seed: int option
          Target: int option
          Mode: bool option
          Help: bool }

    /// Default (empty) CLI options
    let private defaultOptions =
        { Variant = None; Players = None; Humans = None
          Seed = None; Target = None; Mode = None; Help = false }

    /// Parse command-line arguments recursively
    let private parseArgs (args: string array) : CliOptions =
        let argList = args |> Array.toList
        let rec parse (opts: CliOptions) = function
            | ("--variant" | "-v") :: value :: rest ->
                let v =
                    match value.ToLowerInvariant() with
                    | "standard" | "kasino" -> Some StandardKasino
                    | "misa" | "misa-kasino" | "laisto" | "laistokasino" -> Some LaistoKasino
                    | _ -> None
                parse { opts with Variant = v |> Option.orElse opts.Variant } rest
            | ("--players" | "-p") :: value :: rest ->
                let p =
                    match Int32.TryParse(value) with
                    | true, n when n >= 2 && n <= 4 -> Some n
                    | _ -> None
                parse { opts with Players = p |> Option.orElse opts.Players } rest
            | ("--humans" | "-h") :: value :: rest ->
                let h =
                    match Int32.TryParse(value) with
                    | true, n when n >= 0 && n <= 1 -> Some n
                    | _ -> None
                parse { opts with Humans = h |> Option.orElse opts.Humans } rest
            | ("--seed" | "-s") :: value :: rest ->
                let s =
                    match Int32.TryParse(value) with
                    | true, n -> Some n
                    | _ -> None
                parse { opts with Seed = s |> Option.orElse opts.Seed } rest
            | ("--target" | "-t") :: value :: rest ->
                let t =
                    match Int32.TryParse(value) with
                    | true, n when n >= 1 -> Some n
                    | _ -> None
                parse { opts with Target = t |> Option.orElse opts.Target } rest
            | ("--mode" | "-m") :: value :: rest ->
                let m =
                    match value.ToLowerInvariant() with
                    | "novice" | "n" -> Some true
                    | "advanced" | "a" -> Some false
                    | _ -> None
                parse { opts with Mode = m |> Option.orElse opts.Mode } rest
            | "--help" :: rest ->
                parse { opts with Help = true } rest
            | _ :: rest ->
                parse opts rest
            | [] ->
                opts
        parse defaultOptions argList

    /// Display help message
    let private displayHelp () =
        AnsiConsole.MarkupLine("[bold yellow]Kasino - Finnish Card Game with Quantum AI[/]")
        AnsiConsole.WriteLine()
        AnsiConsole.MarkupLine("[cyan]Usage:[/]")
        AnsiConsole.MarkupLine("  Kasino [OPTIONS]")
        AnsiConsole.WriteLine()
        AnsiConsole.MarkupLine("[cyan]Options:[/]")
        AnsiConsole.MarkupLine("  --variant, -v <variant>   Game variant: standard, laisto (default: interactive)")
        AnsiConsole.MarkupLine("  --players, -p <2-4>       Number of players (default: interactive)")
        AnsiConsole.MarkupLine("  --humans, -h <0-1>        Number of human players (default: interactive)")
        AnsiConsole.MarkupLine("  --mode, -m <mode>         Play mode: novice (show hints), advanced (default: interactive)")
        AnsiConsole.MarkupLine("  --seed, -s <int>          Random seed for reproducible games")
        AnsiConsole.MarkupLine("  --target, -t <int>        Target score to end game (default: 16)")
        AnsiConsole.MarkupLine("  --help                    Show this help")
        AnsiConsole.WriteLine()
        AnsiConsole.MarkupLine("[cyan]Examples:[/]")
        AnsiConsole.MarkupLine("  Kasino                                       # Interactive setup")
        AnsiConsole.MarkupLine("  Kasino -v standard -p 2 -h 1                 # Standard 2-player, 1 human")
        AnsiConsole.MarkupLine("  Kasino -v standard -p 2 -h 1 -m advanced     # Advanced mode (no hints)")
        AnsiConsole.MarkupLine("  Kasino -v laisto -p 3 -h 0                   # Laistokasino, 3 quantum CPUs")
        AnsiConsole.MarkupLine("  Kasino -v standard -p 4 -h 0 -s 42           # Reproducible AI-only game")
        AnsiConsole.MarkupLine("  Kasino -v standard -p 2 -h 0 -s 42 -t 8     # Quick game, target 8 pts")

    /// Interactive setup menu
    let private interactiveSetup () =
        AnsiConsole.Clear()

        let rule = Rule("[bold yellow]Kasino - Finnish Card Game with Quantum AI[/]")
        rule.Style <- Style.Parse("yellow")
        AnsiConsole.Write(rule)
        AnsiConsole.WriteLine()

        // Choose variant
        let variantChoice =
            AnsiConsole.Prompt(
                SelectionPrompt<string>()
                    .Title("[cyan]Select game variant:[/]")
                    .AddChoices([
                        "Standard Kasino - Capture cards, earn most points!"
                        "Laistokasino - Avoid capturing points!"
                    ]))

        let variant =
            if variantChoice.StartsWith("Standard", StringComparison.Ordinal) then
                StandardKasino
            else
                LaistoKasino

        // Choose player count
        let playerCount =
            AnsiConsole.Prompt(
                SelectionPrompt<string>()
                    .Title("[cyan]Number of players:[/]")
                    .AddChoices([
                        "2 players"
                        "3 players"
                        "4 players"
                    ]))

        let players =
            match playerCount.[0] with
            | '2' -> 2
            | '3' -> 3
            | '4' -> 4
            | _ -> 2

        // Choose human count
        let humanChoice =
            AnsiConsole.Prompt(
                SelectionPrompt<string>()
                    .Title("[cyan]Your role:[/]")
                    .AddChoices([
                        "Play as human (1 human + rest quantum CPUs)"
                        "Watch quantum CPUs play (all AI)"
                    ]))

        let humans =
            if humanChoice.StartsWith("Play", StringComparison.Ordinal) then 1 else 0

        // Choose play mode (only relevant for human players)
        let noviceMode =
            if humans > 0 then
                let modeChoice =
                    AnsiConsole.Prompt(
                        SelectionPrompt<string>()
                            .Title("[cyan]Play mode:[/]")
                            .AddChoices([
                                "Novice - Show capture hints for each card"
                                "Advanced - Just show your cards (experienced players)"
                            ]))
                modeChoice.StartsWith("Novice", StringComparison.Ordinal)
            else
                true  // Doesn't matter for AI-only games

        AnsiConsole.WriteLine()

        { Variant = variant
          PlayerCount = players
          HumanCount = humans
          NoviceMode = noviceMode
          Seed = None
          TargetScore = 16
          Backend = Some (LocalBackendFactory.createUnified()) }

    [<EntryPoint>]
    let main args =
        try
            let opts = parseArgs args

            if opts.Help then
                displayHelp ()
                0
            else
                let targetScore = opts.Target |> Option.defaultValue 16

                let config : GameConfig =
                    // Create quantum backend for capture computation
                    let backend = Some (LocalBackendFactory.createUnified())

                    // If all args provided, use them directly
                    match opts.Variant, opts.Players, opts.Humans with
                    | Some v, Some p, Some h ->
                        { Variant = v
                          PlayerCount = p
                          HumanCount = h
                          NoviceMode = opts.Mode |> Option.defaultValue true
                          Seed = opts.Seed
                          TargetScore = targetScore
                          Backend = backend }
                    | _ ->
                        // Interactive setup
                        let cfg = interactiveSetup ()
                        { cfg with
                            Seed = opts.Seed
                            TargetScore = targetScore
                            NoviceMode = opts.Mode |> Option.defaultWith (fun () -> cfg.NoviceMode) }

                GameLoop.runGame config

                AnsiConsole.WriteLine()
                AnsiConsole.MarkupLine("[bold green]Kiitos pelaamisesta! (Thanks for playing!)[/]")
                AnsiConsole.WriteLine()
                0
        with
        | ex ->
            AnsiConsole.MarkupLine(sprintf "[red]Error: %s[/]" (Markup.Escape(ex.Message)))
            AnsiConsole.MarkupLine(sprintf "[grey]%s[/]" (Markup.Escape(ex.StackTrace)))
            1
