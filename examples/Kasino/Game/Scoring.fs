namespace FSharp.Azure.Quantum.Examples.Kasino

/// Scoring for Kasino card game.
/// Points are awarded at the end of a round:
///   - Most cards:    1 point (ties: no one gets it)
///   - Most spades:   2 points (ties: no one gets it)
///   - Each Ace:      1 point
///   - Diamond 10:    2 points
///   - Spade 2:       1 point
///   - Each sweep:    1 point
module Scoring =

    /// Score breakdown for one player
    type ScoreBreakdown =
        { MostCards: int
          MostSpades: int
          Aces: int
          DiamondTen: int
          SpadeTwo: int
          Sweeps: int
          Total: int }

    /// Calculate scores for all players at end of a round
    let calculateScores (players: Player list) : (Player * ScoreBreakdown) list =
        let cardCounts =
            players |> List.map (fun p -> p, p.CapturedCards.Length)

        let spadeCounts =
            players |> List.map (fun p -> p, p.CapturedCards |> List.filter Cards.isSpade |> List.length)

        // Most cards: 1 point (only if unique maximum)
        let maxCards = cardCounts |> List.map snd |> List.max
        let mostCardsWinners = cardCounts |> List.filter (fun (_, c) -> c = maxCards)
        let uniqueMostCards = mostCardsWinners.Length = 1

        // Most spades: 2 points (only if unique maximum)
        let maxSpades = spadeCounts |> List.map snd |> List.max
        let mostSpadesWinners = spadeCounts |> List.filter (fun (_, c) -> c = maxSpades)
        let uniqueMostSpades = mostSpadesWinners.Length = 1

        // Compute non-sweep scores per player
        let intermediate =
            players
            |> List.map (fun player ->
                let myCards = player.CapturedCards.Length
                let mySpades = player.CapturedCards |> List.filter Cards.isSpade |> List.length

                let mostCardsPoints =
                    if uniqueMostCards && myCards = maxCards then 1 else 0

                let mostSpadesPoints =
                    if uniqueMostSpades && mySpades = maxSpades then 2 else 0

                let acePoints =
                    player.CapturedCards |> List.filter Cards.isAce |> List.length

                let diamondTenPoints =
                    if player.CapturedCards |> List.exists Cards.isDiamondTen then 2 else 0

                let spadeTwoPoints =
                    if player.CapturedCards |> List.exists Cards.isSpadeTwo then 1 else 0

                (player, mostCardsPoints, mostSpadesPoints, acePoints, diamondTenPoints, spadeTwoPoints))

        // Sweep deduction: if all players have at least 1 sweep, deduct the minimum
        // from everyone. This normalizes sweeps so universal sweeps don't give advantage.
        let minSweeps =
            if List.isEmpty players then 0
            else players |> List.map (fun p -> p.Sweeps) |> List.min

        intermediate
        |> List.map (fun (player, mostCardsPoints, mostSpadesPoints, acePoints, diamondTenPoints, spadeTwoPoints) ->
            let sweepPoints = player.Sweeps - minSweeps

            let total =
                mostCardsPoints + mostSpadesPoints + acePoints
                + diamondTenPoints + spadeTwoPoints + sweepPoints

            let breakdown =
                { MostCards = mostCardsPoints
                  MostSpades = mostSpadesPoints
                  Aces = acePoints
                  DiamondTen = diamondTenPoints
                  SpadeTwo = spadeTwoPoints
                  Sweeps = sweepPoints
                  Total = total }

            (player, breakdown))

    /// Maximum possible score in a round
    let maxRoundScore = 16  // 1 + 2 + 4 + 2 + 1 + sweeps(variable) = at least 10
