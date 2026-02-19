namespace FSharp.Azure.Quantum.Examples.Kasino

open System

/// Card suit
type Suit =
    | Spades
    | Hearts
    | Diamonds
    | Clubs

/// Card rank (1-13)
type Rank =
    | Ace
    | Two
    | Three
    | Four
    | Five
    | Six
    | Seven
    | Eight
    | Nine
    | Ten
    | Jack
    | Queen
    | King

/// A playing card with suit and rank
type Card = { Suit: Suit; Rank: Rank }

/// Game variant
type GameVariant =
    | StandardKasino
    | LaistoKasino

/// Player type
type PlayerType =
    | Human
    | QuantumCPU

/// A player in the game
type Player =
    { Name: string
      Type: PlayerType
      Hand: Card list
      CapturedCards: Card list
      Sweeps: int }

/// Result of playing a card
type PlayResult =
    | Capture of handCard: Card * captured: Card list * sweep: bool
    | Place of handCard: Card

module Cards =

    /// Rank display name
    let rankName = function
        | Ace   -> "A"
        | Two   -> "2"
        | Three -> "3"
        | Four  -> "4"
        | Five  -> "5"
        | Six   -> "6"
        | Seven -> "7"
        | Eight -> "8"
        | Nine  -> "9"
        | Ten   -> "10"
        | Jack  -> "J"
        | Queen -> "Q"
        | King  -> "K"

    /// Suit display symbol
    let suitSymbol = function
        | Spades   -> "\u2660"  // black spade
        | Hearts   -> "\u2665"  // black heart
        | Diamonds -> "\u2666"  // black diamond
        | Clubs    -> "\u2663"  // black club

    /// Suit display name
    let suitName = function
        | Spades   -> "Spades"
        | Hearts   -> "Hearts"
        | Diamonds -> "Diamonds"
        | Clubs    -> "Clubs"

    /// Card display string (e.g. "A\u2660", "10\u2666")
    let cardDisplay (card: Card) =
        sprintf "%s%s" (rankName card.Rank) (suitSymbol card.Suit)

    /// Card numeric value on the table (Ace = 1)
    let tableValue = function
        | Ace   -> 1
        | Two   -> 2
        | Three -> 3
        | Four  -> 4
        | Five  -> 5
        | Six   -> 6
        | Seven -> 7
        | Eight -> 8
        | Nine  -> 9
        | Ten   -> 10
        | Jack  -> 11
        | Queen -> 12
        | King  -> 13

    /// Card value in hand (Ace = 14, Spade 2 = 15, Diamond 10 = 16)
    let handValue (card: Card) =
        match card.Suit, card.Rank with
        | _,        Ace -> 14
        | Spades,   Two -> 15
        | Diamonds, Ten -> 16
        | _,        _   -> tableValue card.Rank

    /// All ranks in a standard deck
    let allRanks =
        [ Ace; Two; Three; Four; Five; Six; Seven; Eight; Nine; Ten; Jack; Queen; King ]

    /// All suits
    let allSuits = [ Spades; Hearts; Diamonds; Clubs ]

    /// Create a full 52-card deck
    let createDeck () =
        [ for suit in allSuits do
            for rank in allRanks do
                yield { Suit = suit; Rank = rank } ]

    /// Shuffle a deck using Fisher-Yates
    let shuffle (rng: Random) (deck: Card list) =
        let arr = deck |> Array.ofList
        for i in arr.Length - 1 .. -1 .. 1 do
            let j = rng.Next(i + 1)
            let tmp = arr.[i]
            arr.[i] <- arr.[j]
            arr.[j] <- tmp
        arr |> Array.toList

    /// Deal n cards from the top of the deck, returning (dealt, remaining)
    let deal (n: int) (deck: Card list) : Card list * Card list =
        let dealt = deck |> List.take (min n (List.length deck))
        let remaining = deck |> List.skip (List.length dealt)
        (dealt, remaining)

    /// Check if a card is a spade
    let isSpade (card: Card) = card.Suit = Spades

    /// Check if a card is the special Spade 2 (Pata Kakkonen)
    let isSpadeTwo (card: Card) = card.Suit = Spades && card.Rank = Two

    /// Check if a card is the special Diamond 10 (Ruutu Kymppi)
    let isDiamondTen (card: Card) = card.Suit = Diamonds && card.Rank = Ten

    /// Check if a card is an ace
    let isAce (card: Card) = card.Rank = Ace

    /// Static scoring value of a captured card (context-free baseline).
    /// Combines direct scoring with uniform fractional contributions:
    ///   - Any card:  1/52 toward "most cards" (1 pt / 52 cards)
    ///   - Any spade: 2/13 toward "most spades" (2 pts / 13 spades)
    ///   - Direct: Ace=1, ♦10=2, ♠2=1
    ///
    /// Examples:
    ///   ♠2  = 1/52 + 2/13 + 1 ≈ 1.173
    ///   ♦10 = 1/52 + 2       ≈ 2.019
    ///   A♠  = 1/52 + 2/13 + 1 ≈ 1.173
    ///   A♥  = 1/52 + 1       ≈ 1.019
    ///   7♠  = 1/52 + 2/13    ≈ 0.173
    ///   7♥  = 1/52           ≈ 0.019
    ///
    /// The three card values:
    ///   tableValue   - face value on the table (A=1, 2-10, J=11, Q=12, K=13)
    ///   handValue    - capture power from hand (A=14, ♠2=15, ♦10=16, rest=tableValue)
    ///   scoringValue - points when captured (see above)
    let scoringValue (card: Card) : float =
        let direct =
            if isDiamondTen card then 2.0      // ♦10: 2 points
            elif isSpadeTwo card then 1.0      // ♠2: 1 point (also counted as spade below)
            elif isAce card then 1.0           // Each Ace: 1 point
            else 0.0
        let cardFraction = 1.0 / 52.0          // any card toward "most cards" (1 pt)
        let spadeFraction =
            if isSpade card then 2.0 / 13.0    // spade toward "most spades" (2 pts)
            else 0.0
        direct + cardFraction + spadeFraction

    /// Dynamic scoring value that considers the current game context.
    /// The marginal value of a spade increases when you're close to having
    /// the majority; the marginal value of a card increases similarly.
    ///
    /// Parameters:
    ///   myCards       - number of cards I've already captured
    ///   mySpades      - number of spades I've already captured
    ///   opponentCards - highest card count among opponents
    ///   opponentSpades - highest spade count among opponents
    ///   cardsRemaining - cards still to be played this round
    let scoringValueInContext
        (myCards: int)
        (mySpades: int)
        (opponentCards: int)
        (opponentSpades: int)
        (cardsRemaining: int)
        (card: Card) : float =

        let direct =
            if isDiamondTen card then 2.0
            elif isSpadeTwo card then 1.0
            elif isAce card then 1.0
            else 0.0

        // "Most cards" marginal value:
        // If I capture this card, how much closer does it bring me to (or keep me at)
        // the "most cards" bonus? Value increases as the race tightens.
        let cardGap = float (myCards + 1 - opponentCards)
        let cardMargin =
            if cardsRemaining <= 0 then
                // No more cards to play — this is the last chance
                if cardGap >= 1.0 then 1.0 else 0.0
            else
                // Sigmoid-ish: value rises as we approach parity with opponent.
                // When ahead by many, marginal value is low; when close or behind, it's high.
                let halfRemaining = float cardsRemaining / 2.0
                1.0 / (1.0 + exp (-(cardGap / halfRemaining) * 3.0))

        // "Most spades" marginal value (same logic, 2 pts at stake):
        let spadeMargin =
            if isSpade card then
                let spadeGap = float (mySpades + 1 - opponentSpades)
                let spadesRemaining =
                    // Rough estimate: ~1/4 of remaining cards are spades
                    max 1 (cardsRemaining / 4)
                let halfSpadesRem = float spadesRemaining / 2.0
                2.0 / (1.0 + exp (-(spadeGap / halfSpadesRem) * 3.0))
            else
                0.0

        direct + cardMargin + spadeMargin
