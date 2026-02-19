namespace FSharp.Azure.Quantum.Examples.Kasino

open System
open Spectre.Console

/// Console UI rendering for Kasino card game
module Renderer =

    /// Distinct colors for up to 4 players (used in labels and messages)
    let private playerColors = [| "cyan1"; "magenta1"; "darkorange"; "mediumpurple1" |]

    /// Get the color for a player by index (cycles if more than 4)
    let playerColor (playerIndex: int) =
        playerColors.[playerIndex % playerColors.Length]

    /// Get the color for a player by name (looks up index in player list)
    let playerColorByName (players: Player list) (name: string) =
        match players |> List.tryFindIndex (fun p -> p.Name = name) with
        | Some idx -> playerColor idx
        | None -> "white"

    /// Spectre color for a suit (traditional card colors)
    let private suitColor = function
        | Spades   -> "black"
        | Clubs    -> "black"
        | Hearts   -> "red"
        | Diamonds -> "red"

    /// Spectre color for a suit on the green table
    let private suitColorOnTable = function
        | Spades   -> "black on green"
        | Clubs    -> "black on green"
        | Hearts   -> "red on green"
        | Diamonds -> "red on green"

    /// Spectre color for a suit on grey23 background (for play messages with dark cards)
    let private suitColorOnGrey = function
        | Spades   -> "white on grey23"
        | Clubs    -> "white on grey23"
        | Hearts   -> "red on grey23"
        | Diamonds -> "red on grey23"

    /// Spectre color for a suit on blue background (for hand/options — black suits visible)
    let private suitColorOnBlue = function
        | Spades   -> "white on blue"
        | Clubs    -> "white on blue"
        | Hearts   -> "red on blue"
        | Diamonds -> "red on blue"

    /// Render a card with color (for hand display and general use)
    let renderCard (card: Card) : string =
        let color = suitColor card.Suit
        sprintf "[%s]%s[/]" color (Cards.cardDisplay card)

    /// Render a card on the green table
    let private renderCardOnTable (card: Card) : string =
        let color = suitColorOnTable card.Suit
        sprintf "[%s]%s[/]" color (Cards.cardDisplay card)

    /// Render a card on grey background (for play messages — black suits visible)
    let renderCardOnGrey (card: Card) : string =
        let color = suitColorOnGrey card.Suit
        sprintf "[%s]%s[/]" color (Cards.cardDisplay card)

    /// Render a card on blue background (for hand/options — black suits visible)
    let renderCardOnBlue (card: Card) : string =
        let color = suitColorOnBlue card.Suit
        sprintf "[%s]%s[/]" color (Cards.cardDisplay card)

    /// Render a list of cards without indices
    let renderCards (cards: Card list) : string =
        if List.isEmpty cards then
            "[grey](empty)[/]"
        else
            cards |> List.map renderCard |> String.concat "  "

    /// Render a list of cards on grey background (for play messages)
    let renderCardsOnGrey (cards: Card list) : string =
        if List.isEmpty cards then
            "[grey](empty)[/]"
        else
            cards |> List.map renderCardOnGrey |> String.concat "[on grey23]  [/]"

    /// Render a list of cards on blue background (for hand/options)
    let renderCardsOnBlue (cards: Card list) : string =
        if List.isEmpty cards then
            "[grey on blue](empty)[/]"
        else
            cards |> List.map renderCardOnBlue |> String.concat "[on blue]  [/]"

    /// Render a list of cards on the green table (with spacing)
    let private renderCardsOnTable (cards: Card list) : string =
        if List.isEmpty cards then
            "[black on green](empty table)[/]"
        else
            cards |> List.map renderCardOnTable |> String.concat "[on green]  [/]"

    /// Measure the visible length of a Spectre markup string (strips [tag] markup)
    let visibleLength (markup: string) : int =
        System.Text.RegularExpressions.Regex.Replace(markup, @"\[/?[^\]]*\]", "").Length

    /// Pad a single markup line with invisible dots so the background colour fills
    /// to the target width. The dots are rendered in `bgColor on bgColor` so they
    /// are invisible but eliminate the black gap at the end of each line.
    let padLine (bgColor: string) (targetWidth: int) (line: string) : string =
        let visible = visibleLength line
        if visible >= targetWidth then line
        else line + sprintf "[%s on %s]%s[/]" bgColor bgColor (String('.', targetWidth - visible))

    /// Pad every line (split by \n) in a multi-line markup string.
    let padLines (bgColor: string) (targetWidth: int) (content: string) : string =
        content.Split('\n')
        |> Array.map (padLine bgColor targetWidth)
        |> String.concat "\n"

    /// Display the game title
    let displayTitle (variant: GameVariant) =
        let variantName =
            match variant with
            | StandardKasino -> "Kasino"
            | LaistoKasino -> "Laistokasino"
        let rule = Rule(sprintf "[bold yellow]%s - Finnish Card Game with Quantum AI[/]" variantName)
        rule.Style <- Style.Parse("yellow")
        AnsiConsole.Write(rule)
        AnsiConsole.WriteLine()

    /// Display game rules
    let displayRules (variant: GameVariant) =
        let variantText =
            match variant with
            | StandardKasino ->
                "[bold green]Standard Kasino:[/] Capture cards to earn points. " +
                "Play a card from hand to capture table cards whose values sum to your card. " +
                "Whoever earns the most points wins!"
            | LaistoKasino ->
                "[bold red]Laistokasino:[/] Try to AVOID capturing points! " +
                "Same rules as Standard, but you want the FEWEST points. " +
                "You may choose to capture or place on the table, but if you capture, " +
                "you must take ALL matching combinations."

        let scoringText =
            "[bold cyan]Scoring:[/]\n" +
            "  Each Ace: 1 point | 10[red]\u2666[/]: 2 points | 2\u2660: 1 point\n" +
            "  Most cards: 1 point | Most spades: 2 points | Each sweep: 1 point\n" +
            "[bold cyan]Special values in hand:[/] Ace=14, 2\u2660=15, 10[red]\u2666[/]=16"

        let panel = Panel(sprintf "%s\n\n%s" variantText scoringText)
        panel.Header <- PanelHeader("Rules")
        panel.Border <- BoxBorder.Double
        panel.BorderStyle <- Style(foreground = Color.Yellow)
        AnsiConsole.Write(panel)
        AnsiConsole.WriteLine()

    /// Display the current table (green felt background, white edges)
    let displayTable (tableCards: Card list) =
        let cardsContent = renderCardsOnTable tableCards
        let raw = sprintf " %s " cardsContent
        let minInner = max 18 (visibleLength raw)  // inner width (panel width - 2 for border)
        let padded = padLine "green" minInner raw
        let markup = Markup(padded, Style(background = Color.Green))
        let panel = Panel(markup)
        panel.Header <- PanelHeader(sprintf "[bold white on green] Table (%d cards) [/]" (List.length tableCards))
        panel.Border <- BoxBorder.Rounded
        panel.BorderStyle <- Style(foreground = Color.White, background = Color.Green)
        panel.Padding <- Padding(0, 0)
        AnsiConsole.Write(panel)
        AnsiConsole.WriteLine()

    /// Display a player's hand with blue background
    let displayHand (player: Player) (showCards: bool) (_playerIndex: int) =
        let content =
            if showCards then
                renderCardsOnBlue player.Hand
            else
                player.Hand
                |> List.map (fun _ -> "[grey]\u2588\u2588[/]")
                |> String.concat " "
        let raw = sprintf " %s " content
        let padded = padLine "blue" (visibleLength raw) raw
        let markup = Markup(padded, Style(background = Color.Blue))
        let panel = Panel(markup)
        let headerName = if player.Name = "You" then "Your" else sprintf "%s's" player.Name
        panel.Header <- PanelHeader(sprintf "[bold white on blue] %s Hand (%d cards) [/]" headerName (List.length player.Hand))
        panel.Border <- BoxBorder.Rounded
        panel.BorderStyle <- Style(foreground = Color.Blue, background = Color.Blue)
        panel.Padding <- Padding(0, 0)
        AnsiConsole.Write(panel)
        AnsiConsole.WriteLine()

    /// Display game state overview
    let displayGameState (players: Player list) (deckRemaining: int) (dealRound: int) (totalDeals: int) =
        let table = Table()
        table.Border <- TableBorder.Rounded
        table.BorderStyle <- Style(foreground = Color.Grey)
        table.AddColumn("[bold]Player[/]") |> ignore
        table.AddColumn("[bold]Type[/]") |> ignore
        table.AddColumn("[bold]Hand[/]") |> ignore
        table.AddColumn("[bold]Captured[/]") |> ignore
        table.AddColumn("[bold]Sweeps[/]") |> ignore

        for i in 0 .. players.Length - 1 do
            let player = players.[i]
            let color = playerColor i
            let typeStr =
                match player.Type with
                | Human -> sprintf "[%s]Human[/]" color
                | QuantumCPU -> sprintf "[%s]Quantum CPU[/]" color
            table.AddRow(
                sprintf "[%s]%s[/]" color player.Name,
                typeStr,
                sprintf "%d" (List.length player.Hand),
                sprintf "%d" (List.length player.CapturedCards),
                sprintf "%d" player.Sweeps) |> ignore

        AnsiConsole.Write(table)
        AnsiConsole.MarkupLine(sprintf "[grey]Deal round %d/%d | Deck: %d cards remaining[/]" dealRound totalDeals deckRemaining)
        AnsiConsole.WriteLine()

    /// Display a play action (uses grey background so black-suit cards are visible)
    let displayPlay (playerName: string) (players: Player list) (eval: QuantumPlayer.PlayEvaluation) =
        let color = playerColorByName players playerName
        match eval.Result with
        | Capture (handCard, captured, isSweep) ->
            let capturedStr = renderCardsOnGrey captured
            AnsiConsole.MarkupLine(
                sprintf "[on grey23][bold %s]%s[/] plays %s -> captures %s (%d cards, %.1f pts%s)[/]"
                    color
                    playerName
                    (renderCardOnGrey handCard)
                    capturedStr
                    eval.CardsCaptured
                    eval.PointValue
                    (if isSweep then " [bold yellow on grey23]SWEEP![/]" else ""))
        | Place handCard ->
            AnsiConsole.MarkupLine(
                sprintf "[on grey23][bold %s]%s[/] plays %s -> [silver on grey23]placed on table[/][/]"
                    color
                    playerName
                    (renderCardOnGrey handCard))
        AnsiConsole.WriteLine()

    /// Display a quantum AI thinking indicator
    let displayQuantumThinking (playerName: string) (players: Player list) (handSize: int) =
        let color = playerColorByName players playerName
        AnsiConsole.MarkupLine(
            sprintf "[%s]%s[/] [grey](Quantum CPU) evaluating %d cards via QAOA...[/]"
                color playerName handSize)

    /// Display end-of-round scores (single round breakdown)
    let displayRoundScores (scores: (Player * Scoring.ScoreBreakdown) list) (roundNumber: int) (players: Player list) =
        let table = Table()
        table.Border <- TableBorder.Double
        table.BorderStyle <- Style(foreground = Color.Yellow)
        table.AddColumn("[bold]Player[/]") |> ignore
        table.AddColumn("[bold]Cards[/]") |> ignore
        table.AddColumn("[bold]Spades[/]") |> ignore
        table.AddColumn("[bold]Aces[/]") |> ignore
        table.AddColumn("[bold]10[red]\u2666[/][/]") |> ignore
        table.AddColumn("[bold]2\u2660[/]") |> ignore
        table.AddColumn("[bold]Sweeps[/]") |> ignore
        table.AddColumn("[bold]Total[/]") |> ignore

        for (player, breakdown) in scores do
            let color = playerColorByName players player.Name
            table.AddRow(
                sprintf "[%s]%s[/]" color player.Name,
                (if breakdown.MostCards > 0 then sprintf "[green]%d[/]" breakdown.MostCards else "0"),
                (if breakdown.MostSpades > 0 then sprintf "[green]%d[/]" breakdown.MostSpades else "0"),
                (if breakdown.Aces > 0 then sprintf "[green]%d[/]" breakdown.Aces else "0"),
                (if breakdown.DiamondTen > 0 then sprintf "[green]%d[/]" breakdown.DiamondTen else "0"),
                (if breakdown.SpadeTwo > 0 then sprintf "[green]%d[/]" breakdown.SpadeTwo else "0"),
                (if breakdown.Sweeps > 0 then sprintf "[green]%d[/]" breakdown.Sweeps else "0"),
                sprintf "[bold yellow]%d[/]" breakdown.Total) |> ignore

        let panel = Panel(table)
        panel.Header <- PanelHeader(sprintf "Round %d Scores" roundNumber)
        panel.Border <- BoxBorder.Double
        AnsiConsole.Write(panel)
        AnsiConsole.WriteLine()

    /// Display round header with cumulative standings
    let displayRoundHeader
        (roundNumber: int)
        (cumulativeScores: Map<string, int>)
        (variant: GameVariant)
        (targetScore: int) =

        let rule = Rule(sprintf "[bold yellow]Round %d[/]" roundNumber)
        rule.Style <- Style.Parse("yellow")
        AnsiConsole.Write(rule)

        if roundNumber > 1 then
            AnsiConsole.MarkupLine(
                sprintf "[grey]Standings: %s | Target: %d pts[/]"
                    (cumulativeScores
                     |> Map.toList
                     |> List.sortByDescending snd
                     |> List.map (fun (name, score) -> sprintf "%s=%d" name score)
                     |> String.concat ", ")
                    targetScore)
        AnsiConsole.WriteLine()

    /// Display cumulative score table after a round
    let displayCumulativeScores
        (cumulativeScores: Map<string, int>)
        (variant: GameVariant)
        (targetScore: int)
        (players: Player list) =

        let table = Table()
        table.Border <- TableBorder.Heavy
        table.BorderStyle <- Style(foreground = Color.Cyan1)
        table.AddColumn("[bold]Player[/]") |> ignore
        table.AddColumn("[bold]Cumulative Score[/]") |> ignore
        table.AddColumn("[bold]Remaining[/]") |> ignore

        let sorted =
            cumulativeScores
            |> Map.toList
            |> List.sortByDescending snd

        for (name, score) in sorted do
            let remaining = targetScore - score
            let color = playerColorByName players name
            let scoreColor =
                if score >= targetScore then "red"
                elif remaining <= 3 then "yellow"
                else "green"
            table.AddRow(
                sprintf "[%s]%s[/]" color name,
                sprintf "[bold %s]%d[/]" scoreColor score,
                (if remaining > 0 then sprintf "%d to go" remaining else "[red]REACHED![/]")) |> ignore

        let panel = Panel(table)
        panel.Header <- PanelHeader(sprintf "Cumulative Standings (target: %d pts)" targetScore)
        panel.Border <- BoxBorder.Heavy
        panel.BorderStyle <- Style(foreground = Color.Cyan1)
        AnsiConsole.Write(panel)
        AnsiConsole.WriteLine()

    /// Display final game over message
    let displayGameOver
        (cumulativeScores: Map<string, int>)
        (variant: GameVariant)
        (targetScore: int) =

        let sorted =
            cumulativeScores
            |> Map.toList
            |> List.sortByDescending snd

        let reached = sorted |> List.filter (fun (_, s) -> s >= targetScore)

        AnsiConsole.WriteLine()
        let rule = Rule("[bold red]GAME OVER[/]")
        rule.Style <- Style.Parse("red")
        AnsiConsole.Write(rule)
        AnsiConsole.WriteLine()

        match variant with
        | StandardKasino ->
            // Highest score wins; those who reached 16 triggered the end
            let winner, winScore = sorted.Head
            AnsiConsole.MarkupLine(sprintf "[bold green]%s wins the game with %d points![/]" winner winScore)
            for (name, score) in sorted.Tail do
                AnsiConsole.MarkupLine(sprintf "[grey]  %s: %d points[/]" name score)
        | LaistoKasino ->
            // Those who reached 16 lose; lowest score among remaining wins
            for (name, score) in reached do
                AnsiConsole.MarkupLine(sprintf "[bold red]%s reached %d points and is OUT![/]" name score)
            let survivors = sorted |> List.filter (fun (_, s) -> s < targetScore)
            if List.isEmpty survivors then
                AnsiConsole.MarkupLine("[yellow]Everyone reached the target! Lowest score wins.[/]")
                let winner, winScore = sorted |> List.last
                AnsiConsole.MarkupLine(sprintf "[bold green]%s wins with only %d points![/]" winner winScore)
            else
                let winner, winScore = survivors |> List.last
                AnsiConsole.MarkupLine(sprintf "[bold green]%s wins with only %d points![/]" winner winScore)
        AnsiConsole.WriteLine()

    /// Display dealing animation
    let displayDealing (dealRound: int) (toTable: bool) =
        if toTable then
            AnsiConsole.MarkupLine(sprintf "[grey]--- Deal round %d: dealing 4 cards to each player and 4 to table ---[/]" dealRound)
        else
            AnsiConsole.MarkupLine(sprintf "[grey]--- Deal round %d: dealing 4 cards to each player ---[/]" dealRound)
        AnsiConsole.WriteLine()

    /// Clear screen
    let clear () = AnsiConsole.Clear()

    /// Wait for key press
    let waitForKey () =
        if not Console.IsInputRedirected then
            AnsiConsole.Markup("[grey]Press any key...[/]")
            Console.ReadKey(true) |> ignore
            AnsiConsole.WriteLine()
