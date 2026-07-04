namespace FSharp.Azure.Quantum.Examples.Kasino

open FSharp.Azure.Quantum.Core

/// Quantum CPU player for Kasino.
/// Uses the Knapsack solver (iterative QAOA via IQuantumBackend) to evaluate
/// all possible plays and select the optimal card.
/// In Standard Kasino, maximizes points captured.
/// In Misa-Kasino, minimizes points captured (forced capture if no safe play).
module QuantumPlayer =

    /// Snapshot of the game state visible to the AI when making a decision
    type GameContext =
        { MyCards: int              // cards I've already captured
          MySpades: int             // spades I've already captured
          OpponentCards: int        // highest card count among opponents
          OpponentSpades: int       // highest spade count among opponents
          CardsRemaining: int }     // cards still in deck + all hands combined

    /// Evaluation of playing a single hand card with a specific capture option
    type PlayEvaluation =
        { HandCard: Card
          Result: PlayResult
          PointValue: float
          CardsCaptured: int
          IsSweep: bool
          CaptureOptions: Rules.CaptureOption list  // all available capture options (empty for Place)
          ChosenOption: Rules.CaptureOption option } // the selected capture option (None for Place)

    /// Evaluate a specific capture option for a hand card (static scoring)
    let private evaluateOption (handCard: Card) (option: Rules.CaptureOption) (tableCards: Card list) : PlayEvaluation =
        let result, _ = Rules.resolveCapture handCard option tableCards
        match result with
        | Capture (_, captured, isSweep) ->
            // The played card is banked together with the capture (see
            // GameLoop applyPlay), so its points count too.
            let points =
                Rules.capturePointValue (handCard :: captured)
                + (if isSweep then Rules.sweepBonus else 0.0)
            { HandCard = handCard
              Result = result
              PointValue = points
              CardsCaptured = captured.Length
              IsSweep = isSweep
              CaptureOptions = []  // filled in by caller
              ChosenOption = Some option }
        | Place _ ->
            { HandCard = handCard
              Result = result
              PointValue = 0.0
              CardsCaptured = 0
              IsSweep = false
              CaptureOptions = []
              ChosenOption = None }

    /// Evaluate a specific capture option with context-aware scoring
    let private evaluateOptionInContext (ctx: GameContext) (handCard: Card) (option: Rules.CaptureOption) (tableCards: Card list) : PlayEvaluation =
        let result, _ = Rules.resolveCapture handCard option tableCards
        match result with
        | Capture (_, captured, isSweep) ->
            let scoreFn =
                Cards.scoringValueInContext
                    ctx.MyCards ctx.MySpades
                    ctx.OpponentCards ctx.OpponentSpades
                    ctx.CardsRemaining
            // The played card lands in the pile with the capture, so its
            // value counts exactly like the captured table cards'.
            let points =
                ((handCard :: captured) |> List.sumBy scoreFn)
                + (if isSweep then Rules.sweepBonus else 0.0)
            { HandCard = handCard
              Result = result
              PointValue = points
              CardsCaptured = captured.Length
              IsSweep = isSweep
              CaptureOptions = []
              ChosenOption = Some option }
        | Place _ ->
            { HandCard = handCard
              Result = result
              PointValue = 0.0
              CardsCaptured = 0
              IsSweep = false
              CaptureOptions = []
              ChosenOption = None }

    /// Evaluate what happens when a specific hand card is played (static scoring).
    /// If multiple capture options exist, evaluates the best one (most cards captured).
    /// Uses quantum backend for capture computation via Rules.findCaptureOptions.
    let evaluatePlay (backend: BackendAbstraction.IQuantumBackend option) (handCard: Card) (tableCards: Card list) : PlayEvaluation =
        let options = Rules.findCaptureOptions backend handCard tableCards
        match options with
        | [] ->
            // No capture: place
            { HandCard = handCard
              Result = Place handCard
              PointValue = 0.0
              CardsCaptured = 0
              IsSweep = false
              CaptureOptions = []
              ChosenOption = None }
        | [ single ] ->
            let eval = evaluateOption handCard single tableCards
            { eval with CaptureOptions = options }
        | _ ->
            // Multiple options: evaluate each, pick best by points then card count
            let evals =
                options
                |> List.map (fun opt -> evaluateOption handCard opt tableCards)
                |> List.sortByDescending (fun e ->
                    (e.PointValue, float e.CardsCaptured, (if e.IsSweep then 1.0 else 0.0)))
            let best = evals.Head
            { best with CaptureOptions = options }

    /// Evaluate a play using dynamic context-aware scoring.
    /// If multiple capture options exist, evaluates each and picks the best.
    /// For Standard Kasino, "best" = highest points. For Misa, "best" = lowest points.
    let evaluatePlayInContext (backend: BackendAbstraction.IQuantumBackend option) (ctx: GameContext) (variant: GameVariant) (handCard: Card) (tableCards: Card list) : PlayEvaluation =
        let options = Rules.findCaptureOptions backend handCard tableCards
        match options with
        | [] ->
            { HandCard = handCard
              Result = Place handCard
              PointValue = 0.0
              CardsCaptured = 0
              IsSweep = false
              CaptureOptions = []
              ChosenOption = None }
        | [ single ] ->
            let eval = evaluateOptionInContext ctx handCard single tableCards
            { eval with CaptureOptions = options }
        | _ ->
            let evals =
                options
                |> List.map (fun opt -> evaluateOptionInContext ctx handCard opt tableCards)
            let best =
                match variant with
                | StandardKasino ->
                    // Maximize: highest points, then most cards, then sweep preference
                    evals
                    |> List.sortByDescending (fun e ->
                        (e.PointValue, float e.CardsCaptured, (if e.IsSweep then 1.0 else 0.0)))
                    |> List.head
                | LaistoKasino ->
                    // Minimize: lowest points, then fewest cards, avoid sweeps
                    evals
                    |> List.sortBy (fun e ->
                        (e.PointValue, float e.CardsCaptured, (if e.IsSweep then 1.0 else 0.0)))
                    |> List.head
            { best with CaptureOptions = options }

    /// Evaluate all cards in hand against the table (static, for human display)
    let evaluateAllPlays (backend: BackendAbstraction.IQuantumBackend option) (hand: Card list) (tableCards: Card list) : PlayEvaluation list =
        hand |> List.map (fun c -> evaluatePlay backend c tableCards)

    /// Evaluate all cards in hand with game context (for AI decisions)
    let evaluateAllPlaysInContext (backend: BackendAbstraction.IQuantumBackend option) (ctx: GameContext) (variant: GameVariant) (hand: Card list) (tableCards: Card list) : PlayEvaluation list =
        hand |> List.map (fun c -> evaluatePlayInContext backend ctx variant c tableCards)

    /// Choose the best card for Standard Kasino (maximize captured points).
    /// The AI automatically picks the best capture option when overlapping combos exist.
    /// Strategy:
    ///   1. If any card captures, pick the one with highest point value
    ///   2. Prefer sweeps (bonus point)
    ///   3. Among captures with same points, prefer capturing more cards
    ///   4. If no capture possible, place the lowest scoringValue card
    let chooseBestStandard (backend: BackendAbstraction.IQuantumBackend option) (ctx: GameContext) (hand: Card list) (tableCards: Card list) : PlayEvaluation =
        let evals = evaluateAllPlaysInContext backend ctx StandardKasino hand tableCards
        let captures = evals |> List.filter (fun e -> e.CardsCaptured > 0)

        if not (List.isEmpty captures) then
            // Pick best capture: highest points, then most cards, then sweep preference
            captures
            |> List.sortByDescending (fun e ->
                (e.PointValue, float e.CardsCaptured, (if e.IsSweep then 1.0 else 0.0)))
            |> List.head
        else
            // No capture possible: place card that gives opponent least advantage.
            // Sort by scoringValue ascending — place the least valuable card first.
            // Add tableValue as tiebreaker (place lower face-value cards first).
            let scoreFn =
                Cards.scoringValueInContext
                    ctx.MyCards ctx.MySpades
                    ctx.OpponentCards ctx.OpponentSpades
                    ctx.CardsRemaining
            evals
            |> List.sortBy (fun e ->
                let sv = scoreFn e.HandCard
                let tv = float (Cards.tableValue e.HandCard.Rank)
                (sv, tv))
            |> List.head

    /// Choose the best card for Laistokasino (minimize captured points).
    /// Strategy:
    ///   1. If any card does NOT capture, prefer placing it (avoids points)
    ///      (high-value non-special cards first - they're safe to discard)
    ///   2. If ALL cards capture, pick the one with lowest point value
    ///   3. Avoid sweeps if possible (they give bonus points)
    /// Note: In Laistokasino capture is mandatory — a capture-capable card
    /// always captures when played — so avoiding points means choosing which
    /// card to play, not whether to capture with it.
    let chooseBestMisa (backend: BackendAbstraction.IQuantumBackend option) (ctx: GameContext) (hand: Card list) (tableCards: Card list) : PlayEvaluation =
        let evals = evaluateAllPlaysInContext backend ctx LaistoKasino hand tableCards
        let nonCaptures = evals |> List.filter (fun e -> e.CardsCaptured = 0)

        if not (List.isEmpty nonCaptures) then
            // Prefer not capturing — avoids accumulating points.
            // Shed the most dangerous card first: a hoarded point card
            // (ace, 2♠, 10♦, spades) tends to find a forced capture late in
            // the deal and lands in our own pile — the worst Laisto outcome.
            // Discarding it instead pushes the risk onto whoever captures it.
            // Among equally harmless cards, shed the biggest one.
            let scoreFn =
                Cards.scoringValueInContext
                    ctx.MyCards ctx.MySpades
                    ctx.OpponentCards ctx.OpponentSpades
                    ctx.CardsRemaining
            nonCaptures
            |> List.sortByDescending (fun e ->
                (scoreFn e.HandCard, float (Cards.tableValue e.HandCard.Rank)))
            |> List.head
        else
            // All cards capture: minimize damage
            evals
            |> List.sortBy (fun e ->
                (e.PointValue, float e.CardsCaptured, (if e.IsSweep then 1.0 else 0.0)))
            |> List.head

    /// Choose the best play based on game variant
    let chooseBest (backend: BackendAbstraction.IQuantumBackend option) (variant: GameVariant) (ctx: GameContext) (hand: Card list) (tableCards: Card list) : PlayEvaluation =
        match variant with
        | StandardKasino -> chooseBestStandard backend ctx hand tableCards
        | LaistoKasino -> chooseBestMisa backend ctx hand tableCards
