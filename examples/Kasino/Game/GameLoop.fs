namespace FSharp.Azure.Quantum.Examples.Kasino

open System
open Spectre.Console
open FSharp.Azure.Quantum.Core

/// Main game loop for Kasino
module GameLoop =

    /// Game configuration
    type GameConfig =
        { Variant: GameVariant
          PlayerCount: int
          HumanCount: int  // 0 or 1
          NoviceMode: bool  // true = show capture previews, false = cards only (advanced)
          Seed: int option
          TargetScore: int  // Game ends when a player reaches this (default 16)
          Backend: BackendAbstraction.IQuantumBackend option }

    /// Full game state
    type GameState =
        { Players: Player list
          Table: Card list
          Deck: Card list
          CurrentPlayerIndex: int
          DealRound: int
          TotalDeals: int
          LastCapturer: int option  // Index of last player who captured
          Variant: GameVariant }

    /// Calculate total deal rounds based on player count
    /// 52 cards, 4 per player per deal + 4 to table on first deal
    let totalDealRounds (playerCount: int) : int =
        // First deal: 4 * players + 4 table = 4*(players+1) cards
        // Subsequent deals: 4 * players cards
        // Total: 4*(players+1) + (rounds-1)*4*players = 52
        // 4*players + 4 + 4*players*rounds - 4*players = 52
        // 4 + 4*players*rounds = 52
        // rounds = 48 / (4*players) = 12 / players
        12 / playerCount

    /// Create initial players
    let createPlayers (config: GameConfig) : Player list =
        let quantumNames = [| "Quantum-1"; "Quantum-2"; "Quantum-3"; "Quantum-4" |]
        let humanCount = config.HumanCount
        let cpuSlots =
            [ for i in 0 .. config.PlayerCount - 1 do
                if i < humanCount then
                    yield (i, "You", Human)
                else
                    yield (i, quantumNames.[i - humanCount], QuantumCPU) ]
        cpuSlots
        |> List.map (fun (_, name, ptype) ->
            { Name = name
              Type = ptype
              Hand = []
              CapturedCards = []
              Sweeps = 0 })

    /// Deal cards to all players and optionally the table
    let dealRound (state: GameState) (isFirstDeal: bool) : GameState =
        let mutable deck = state.Deck
        let mutable players = state.Players |> Array.ofList

        // Deal 4 cards to each player (2 at a time, 2 rounds, as per Kasino tradition)
        for _ in 1 .. 2 do
            for i in 0 .. players.Length - 1 do
                let (dealt, remaining) = Cards.deal 2 deck
                deck <- remaining
                players.[i] <-
                    { players.[i] with
                        Hand = players.[i].Hand @ dealt }

        // Deal 4 cards to table (only on first deal)
        let table =
            if isFirstDeal then
                let (tableCards, remaining) = Cards.deal 4 deck
                deck <- remaining
                state.Table @ tableCards
            else
                state.Table

        { state with
            Players = players |> Array.toList
            Table = table
            Deck = deck }

    /// How a chosen hand card should be resolved when applied.
    type HumanPlay =
        | TakeOption of Rules.CaptureOption  // capture with this specific option
        | PlaceCard                          // place on the table without capturing
        | AutoPlay                           // let Rules.playCard decide (capture if possible)

    /// Get human player's card choice (returns None if player wants to quit).
    /// Returns (cardIndex, play) — cardIndex refers to the position in the
    /// original (unsorted) hand. In Standard Kasino capturing is optional, so
    /// the player may answer e.g. "3P" to place card 3 even when it captures.
    let rec getHumanChoice (backend: BackendAbstraction.IQuantumBackend option) (player: Player) (playerIndex: int) (tableCards: Card list) (variant: GameVariant) (noviceMode: bool) : (int * HumanPlay) option =
        // Sort hand by handValue ascending for display; track original indices
        let sortedWithOrigIdx =
            player.Hand
            |> List.mapi (fun i c -> (i, c))
            |> List.sortBy (fun (_, c) -> Cards.handValue c)
        let sortedHand = sortedWithOrigIdx |> List.map snd
        let origIndices = sortedWithOrigIdx |> List.map fst

        // Display hand sorted
        let sortedPlayer = { player with Hand = sortedHand }
        Renderer.displayHand sortedPlayer true playerIndex

        // Build special card notes for hand cards
        let specialNote (card: Card) =
            let notes = [
                if Cards.isAce card then yield "Ace: 1pt"
                if Cards.isDiamondTen card then yield "10\u2666: 2pts"
                if Cards.isSpadeTwo card then yield "2\u2660: 1pt"
            ]
            if List.isEmpty notes then ""
            else sprintf " [yellow on blue](%s)[/]" (String.concat ", " notes)

        // Precompute evaluations for sorted hand cards (needed for capture option detection in both modes)
        let evals = QuantumPlayer.evaluateAllPlays backend sortedHand tableCards

        if noviceMode then
            // Novice: show full capture previews inside a blue panel
            let lines = ResizeArray<string>()
            for i in 0 .. sortedHand.Length - 1 do
                let card = sortedHand.[i]
                let eval = evals.[i]
                match eval.Result with
                | Capture (_, captured, isSweep) ->
                    let optionNote =
                        if eval.CaptureOptions.Length > 1 then
                            sprintf " [yellow on blue](%d capture options)[/]" eval.CaptureOptions.Length
                        else ""
                    lines.Add(
                        sprintf " [cyan on blue]%d:[/] %s [white on blue]->[/] [white on blue]captures[/] %s%s%s%s"
                            (i + 1)
                            (Renderer.renderCardOnBlue card)
                            (Renderer.renderCardsOnBlue captured)
                            (if isSweep then " [bold yellow on blue]SWEEP![/]" else "")
                            optionNote
                            (specialNote card))
                | Place _ ->
                    lines.Add(
                        sprintf " [cyan on blue]%d:[/] %s [white on blue]->[/] [silver on blue]place on table[/]%s"
                            (i + 1)
                            (Renderer.renderCardOnBlue card)
                            (specialNote card))
            let joined = String.concat "\n" lines
            let padded = Renderer.padLines "blue" (lines |> Seq.map Renderer.visibleLength |> Seq.max) joined
            let markup = Markup(padded, Style(background = Color.Blue))
            let panel = Panel(markup)
            panel.Header <- PanelHeader("[bold white on blue] Your options [/]")
            panel.Border <- BoxBorder.Rounded
            panel.BorderStyle <- Style(foreground = Color.Blue, background = Color.Blue)
            panel.Padding <- Padding(0, 0)
            AnsiConsole.Write(panel)
            AnsiConsole.WriteLine()

            // Variant-specific strategic hints
            match variant with
            | LaistoKasino ->
                let nonCaptures = evals |> List.filter (fun e -> e.CardsCaptured = 0)
                if List.isEmpty nonCaptures then
                    AnsiConsole.MarkupLine("[red]All cards capture (capture is forced in Laisto)! Pick the one with fewest points.[/]")
                else
                    AnsiConsole.MarkupLine("[green]Cards that capture are taken automatically — place a non-capturing card to stay safe.[/]")
                AnsiConsole.WriteLine()
            | StandardKasino ->
                if evals |> List.exists (fun e -> e.CardsCaptured > 0) then
                    AnsiConsole.MarkupLine("[grey]Capturing is optional: add P to place instead (e.g. 2P).[/]")
                    AnsiConsole.WriteLine()
        else
            // Advanced: just show numbered cards, no previews, inside a blue panel
            let lines = ResizeArray<string>()
            for i in 0 .. sortedHand.Length - 1 do
                let card = sortedHand.[i]
                lines.Add(
                    sprintf " [cyan on blue]%d:[/] %s%s"
                        (i + 1)
                        (Renderer.renderCardOnBlue card)
                        (specialNote card))
            let joined = String.concat "\n" lines
            let padded = Renderer.padLines "blue" (lines |> Seq.map Renderer.visibleLength |> Seq.max) joined
            let markup = Markup(padded, Style(background = Color.Blue))
            let panel = Panel(markup)
            panel.Header <- PanelHeader("[bold white on blue] Choose a card to play [/]")
            panel.Border <- BoxBorder.Rounded
            panel.BorderStyle <- Style(foreground = Color.Blue, background = Color.Blue)
            panel.Padding <- Padding(0, 0)
            AnsiConsole.Write(panel)
            AnsiConsole.WriteLine()

        let rec getChoice () =
            let placeHint =
                if variant = StandardKasino then ", add P to place instead," else ""
            AnsiConsole.Markup(sprintf "[cyan]Choose card (1-%d)%s or Q to quit:[/] " sortedHand.Length placeHint)
            let input = Console.ReadLine()
            if input = null then
                None  // EOF / redirected input
            else
                let trimmed = input.Trim().ToUpperInvariant()
                if trimmed = "Q" then
                    None
                else
                    // Standard Kasino: capturing is optional — "3P" places card 3
                    // on the table even when it could capture.
                    let placeInstead =
                        variant = StandardKasino && trimmed.Length > 1 && trimmed.EndsWith "P"
                    let numPart =
                        if placeInstead then trimmed.Substring(0, trimmed.Length - 1) else trimmed
                    match Int32.TryParse(numPart) with
                    | true, n when n >= 1 && n <= sortedHand.Length ->
                        let sortedIdx = n - 1
                        let origIdx = origIndices.[sortedIdx]
                        let eval = evals.[sortedIdx]
                        if placeInstead then
                            Some (origIdx, PlaceCard)
                        else
                            match eval.CaptureOptions with
                            | [] ->
                                // No capture previewed. In Standard, place
                                // deterministically — the stochastic capture
                                // enumeration must not surprise-capture at apply
                                // time, and placing is legal there regardless.
                                // In Laisto capture is forced, so let the apply
                                // step re-check.
                                match variant with
                                | StandardKasino -> Some (origIdx, PlaceCard)
                                | LaistoKasino -> Some (origIdx, AutoPlay)
                            | [ single ] ->
                                // Resolve the previewed option deterministically.
                                Some (origIdx, TakeOption single)
                            | options ->
                                match getCaptureOptionChoice options variant tableCards with
                                | Some play -> Some (origIdx, play)
                                | None -> None  // Player quit during sub-choice
                    | _ ->
                        AnsiConsole.MarkupLine("[red]Invalid choice![/]")
                        getChoice ()
        getChoice ()

    /// Ask the human to choose among multiple capture options. In Standard
    /// Kasino the player may also decline the capture and place the card ("0").
    and private getCaptureOptionChoice (options: Rules.CaptureOption list) (variant: GameVariant) (tableCards: Card list) : HumanPlay option =
        AnsiConsole.WriteLine()
        // Single-letter labels only support A-Z; show the biggest captures first
        // and truncate the rest (findCaptureOptions can return up to 64 options).
        let maxShown = 24
        let shown =
            options
            |> List.sortByDescending (fun opt -> opt.Captured.Length)
            |> List.truncate maxShown
        let allowPlace = (variant = StandardKasino)
        let lines = ResizeArray<string>()
        for i in 0 .. shown.Length - 1 do
            let opt = shown.[i]
            let combosStr =
                opt.Combos
                |> List.map (fun combo ->
                    sprintf "[white on blue]{[/]%s[white on blue]}[/]" (Renderer.renderCardsOnBlue combo))
                |> String.concat " [white on blue]+[/] "
            lines.Add(
                sprintf " [yellow on blue]%c:[/] %s [white on blue]= %d cards[/]"
                    (char (int 'A' + i))
                    combosStr
                    opt.Captured.Length)
        if options.Length > shown.Length then
            lines.Add(sprintf " [silver on blue](%d smaller options not shown)[/]" (options.Length - shown.Length))
        if allowPlace then
            lines.Add(" [yellow on blue]0:[/] [silver on blue]place on table instead (no capture)[/]")
        let joined = String.concat "\n" lines
        let padded = Renderer.padLines "blue" (lines |> Seq.map Renderer.visibleLength |> Seq.max) joined
        let markup = Markup(padded, Style(background = Color.Blue))
        let panel = Panel(markup)
        panel.Header <- PanelHeader("[bold yellow on blue] Choose which cards to capture [/]")
        panel.Border <- BoxBorder.Rounded
        panel.BorderStyle <- Style(foreground = Color.Blue, background = Color.Blue)
        panel.Padding <- Padding(0, 0)
        AnsiConsole.Write(panel)
        AnsiConsole.WriteLine()

        let rec getSubChoice () =
            let placeHint = if allowPlace then ", 0 to place," else ""
            AnsiConsole.Markup(sprintf "[yellow]Choose option (A-%c)%s or Q to quit:[/] " (char (int 'A' + shown.Length - 1)) placeHint)
            let input = Console.ReadLine()
            if input = null then
                None
            else
                let trimmed = input.Trim().ToUpperInvariant()
                if trimmed = "Q" then
                    None
                elif allowPlace && trimmed = "0" then
                    Some PlaceCard
                else
                    match trimmed with
                    | s when s.Length = 1 ->
                        let idx = int s.[0] - int 'A'
                        if idx >= 0 && idx < shown.Length then
                            Some (TakeOption shown.[idx])
                        else
                            AnsiConsole.MarkupLine("[red]Invalid choice![/]")
                            getSubChoice ()
                    | _ ->
                        AnsiConsole.MarkupLine("[red]Invalid choice![/]")
                        getSubChoice ()
        getSubChoice ()

    /// Build game context for AI decision-making
    let private buildContext (state: GameState) (playerIdx: int) : QuantumPlayer.GameContext =
        let player = state.Players.[playerIdx]
        let myCards = List.length player.CapturedCards
        let mySpades = player.CapturedCards |> List.filter Cards.isSpade |> List.length
        let opponents = state.Players |> List.mapi (fun i p -> (i, p)) |> List.filter (fun (i, _) -> i <> playerIdx)
        let opponentCards = opponents |> List.map (fun (_, p) -> List.length p.CapturedCards) |> List.max
        let opponentSpades =
            opponents
            |> List.map (fun (_, p) -> p.CapturedCards |> List.filter Cards.isSpade |> List.length)
            |> List.max
        let cardsRemaining =
            List.length state.Deck
            + (state.Players |> List.sumBy (fun p -> List.length p.Hand))
        { MyCards = myCards
          MySpades = mySpades
          OpponentCards = opponentCards
          OpponentSpades = opponentSpades
          CardsRemaining = cardsRemaining }

    /// Play one turn for a player. Returns None if the human chose to quit.
    let playTurn (backend: BackendAbstraction.IQuantumBackend option) (noviceMode: bool) (state: GameState) : GameState option =
        let playerIdx = state.CurrentPlayerIndex
        let player = state.Players.[playerIdx]

        if List.isEmpty player.Hand then
            // No cards to play, skip
            Some { state with
                    CurrentPlayerIndex = (playerIdx + 1) % state.Players.Length }
        else
            // Choose card and how to resolve it
            let choiceResult =
                match player.Type with
                | Human ->
                    getHumanChoice backend player playerIdx state.Table state.Variant noviceMode
                | QuantumCPU ->
                    Renderer.displayQuantumThinking player.Name state.Players (List.length player.Hand)
                    let ctx = buildContext state playerIdx
                    let eval = QuantumPlayer.chooseBest backend state.Variant ctx player.Hand state.Table
                    let idx = player.Hand |> List.findIndex (fun c -> c = eval.HandCard)
                    let action =
                        match eval.ChosenOption with
                        | Some opt -> TakeOption opt
                        | None ->
                            // The AI decided to place. In Standard that is legal
                            // outright (capturing is optional); in Laisto capture
                            // is forced, so re-check at apply time in case the
                            // stochastic enumeration missed a capture.
                            match state.Variant with
                            | StandardKasino -> PlaceCard
                            | LaistoKasino -> AutoPlay
                    Some (idx, action)

            match choiceResult with
            | None -> None  // Player quit
            | Some (idx, action) ->

            let chosenCard = player.Hand.[idx]
            let remainingHand = player.Hand |> List.removeAt idx

            // Resolve the play exactly once — the capture enumeration is
            // stochastic (iterative QAOA), so it must not be re-run between
            // choosing, applying, and displaying a play.
            let result, newTable =
                match action with
                | TakeOption opt ->
                    Rules.resolveCapture chosenCard opt state.Table
                | PlaceCard ->
                    (Place chosenCard, chosenCard :: state.Table)
                | AutoPlay ->
                    let r, t, _ = Rules.playCard backend chosenCard state.Table
                    (r, t)

            // Build the display evaluation from the play that was actually
            // applied. The played card is banked with the capture, so its
            // points count too.
            let eval =
                match result with
                | Capture (_, captured, isSweep) ->
                    { QuantumPlayer.HandCard = chosenCard
                      QuantumPlayer.Result = result
                      QuantumPlayer.PointValue =
                          Rules.capturePointValue (chosenCard :: captured)
                          + (if isSweep then Rules.sweepBonus else 0.0)
                      QuantumPlayer.CardsCaptured = captured.Length
                      QuantumPlayer.IsSweep = isSweep
                      QuantumPlayer.CaptureOptions = []
                      QuantumPlayer.ChosenOption = (match action with TakeOption opt -> Some opt | _ -> None) }
                | Place _ ->
                    { QuantumPlayer.HandCard = chosenCard
                      QuantumPlayer.Result = result
                      QuantumPlayer.PointValue = 0.0
                      QuantumPlayer.CardsCaptured = 0
                      QuantumPlayer.IsSweep = false
                      QuantumPlayer.CaptureOptions = []
                      QuantumPlayer.ChosenOption = None }
            Renderer.displayPlay player.Name state.Players eval

            // Update player state
            let updatedPlayer =
                match result with
                | Capture (_, captured, isSweep) ->
                    { player with
                        Hand = remainingHand
                        CapturedCards = player.CapturedCards @ [chosenCard] @ captured
                        Sweeps = player.Sweeps + (if isSweep then 1 else 0) }
                | Place _ ->
                    { player with
                        Hand = remainingHand }

            let updatedPlayers =
                state.Players
                |> List.mapi (fun i p -> if i = playerIdx then updatedPlayer else p)

            let lastCapturer =
                match result with
                | Capture _ -> Some playerIdx
                | Place _ -> state.LastCapturer

            Some { state with
                    Players = updatedPlayers
                    Table = newTable
                    CurrentPlayerIndex = (playerIdx + 1) % state.Players.Length
                    LastCapturer = lastCapturer }

    /// Check if all players have empty hands (time to deal or end)
    let allHandsEmpty (state: GameState) : bool =
        state.Players |> List.forall (fun p -> List.isEmpty p.Hand)

    /// Run a single round and return the round scores. Returns None if player quit.
    let private runRound
        (config: GameConfig)
        (rng: Random)
        (players: Player list)
        (roundNumber: int)
        (cumulativeScores: Map<string, int>)
        : (Player * Scoring.ScoreBreakdown) list option =

        let deck = Cards.createDeck () |> Cards.shuffle rng
        let totalDeals = totalDealRounds config.PlayerCount

        // Reset players for this round (clear hand, captured, sweeps) but keep names/types
        let freshPlayers =
            players
            |> List.map (fun p ->
                { p with Hand = []; CapturedCards = []; Sweeps = 0 })

        // Rotate dealer: shift starting player by (roundNumber - 1)
        let startIdx = (roundNumber - 1) % freshPlayers.Length

        Renderer.displayRoundHeader roundNumber cumulativeScores config.Variant config.TargetScore

        let mutable state =
            { Players = freshPlayers
              Table = []
              Deck = deck
              CurrentPlayerIndex = startIdx
              DealRound = 0
              TotalDeals = totalDeals
              LastCapturer = None
              Variant = config.Variant }

        let mutable quit = false

        // Deal rounds within this round
        let mutable dealNum = 1
        while dealNum <= totalDeals && not quit do
            let isFirst = dealNum = 1
            state <- { state with DealRound = dealNum }
            Renderer.displayDealing dealNum isFirst

            // Deal cards
            state <- dealRound state isFirst

            Renderer.displayTable state.Table
            Renderer.displayGameState state.Players (List.length state.Deck) dealNum totalDeals

            // Play turns until all hands empty
            while not (allHandsEmpty state) && not quit do
                Renderer.displayTable state.Table
                match playTurn config.Backend config.NoviceMode state with
                | Some newState ->
                    state <- newState
                    // Small pause between turns for readability
                    if config.HumanCount = 0 then
                        System.Threading.Thread.Sleep(300)
                | None ->
                    quit <- true

            dealNum <- dealNum + 1

        if quit then
            None
        else

        // End of round: last capturer gets remaining table cards
        match state.LastCapturer with
        | Some idx ->
            let lastPlayer = state.Players.[idx]
            if not (List.isEmpty state.Table) then
                AnsiConsole.MarkupLine(
                    sprintf "[grey]%s (last to capture) takes remaining table cards: %s[/]"
                        lastPlayer.Name
                        (Renderer.renderCards state.Table))
                AnsiConsole.WriteLine()
                let updatedPlayer =
                    { lastPlayer with
                        CapturedCards = lastPlayer.CapturedCards @ state.Table }
                state <-
                    { state with
                        Players =
                            state.Players
                            |> List.mapi (fun i p -> if i = idx then updatedPlayer else p)
                        Table = [] }
        | None -> ()

        // Calculate and display round scores
        AnsiConsole.WriteLine()
        let scores = Scoring.calculateScores state.Players
        Renderer.displayRoundScores scores roundNumber state.Players

        // Show card count details
        AnsiConsole.MarkupLine("[grey]Card counts:[/]")
        for player in state.Players do
            let spades = player.CapturedCards |> List.filter Cards.isSpade |> List.length
            AnsiConsole.MarkupLine(
                sprintf "  [grey]%s: %d cards (%d spades)[/]"
                    player.Name
                    (List.length player.CapturedCards)
                    spades)
        AnsiConsole.WriteLine()

        Some scores

    /// Run a complete multi-round game
    let runGame (config: GameConfig) : unit =
        let rng =
            match config.Seed with
            | Some s -> Random(s)
            | None -> Random()

        let players = createPlayers config

        Renderer.clear ()
        Renderer.displayTitle config.Variant
        Renderer.displayRules config.Variant
        Renderer.waitForKey ()

        let mutable cumulativeScores =
            players |> List.map (fun p -> p.Name, 0) |> Map.ofList

        let mutable roundNumber = 0
        let mutable gameOver = false

        while not gameOver do
            roundNumber <- roundNumber + 1

            match runRound config rng players roundNumber cumulativeScores with
            | None ->
                // Player quit
                AnsiConsole.MarkupLine("[yellow]Game ended by player.[/]")
                AnsiConsole.WriteLine()
                gameOver <- true
            | Some roundScores ->

            // Accumulate scores
            for (player, breakdown) in roundScores do
                let prev = cumulativeScores.[player.Name]
                cumulativeScores <- cumulativeScores |> Map.add player.Name (prev + breakdown.Total)

            // Display cumulative standings
            Renderer.displayCumulativeScores cumulativeScores config.Variant config.TargetScore players

            // Check if anyone reached the target score
            let reached =
                cumulativeScores |> Map.toList |> List.filter (fun (_, s) -> s >= config.TargetScore)

            if not (List.isEmpty reached) then
                gameOver <- true
                Renderer.displayGameOver cumulativeScores config.Variant config.TargetScore
            else
                Renderer.waitForKey ()
