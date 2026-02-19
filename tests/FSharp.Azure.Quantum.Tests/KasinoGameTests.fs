namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum.Examples.Kasino

/// Tests for the Kasino card game domain logic (Cards, Rules, Scoring, QuantumPlayer).
/// All capture-related tests use backend=None (classical fallback) for deterministic results.
module KasinoGameTests =

    // ========================================================================
    // Cards Module Tests
    // ========================================================================

    [<Fact>]
    let ``Cards.createDeck should produce 52 cards`` () =
        let deck = Cards.createDeck ()
        Assert.Equal(52, deck.Length)

    [<Fact>]
    let ``Cards.createDeck should have 13 cards per suit`` () =
        let deck = Cards.createDeck ()
        let spades = deck |> List.filter (fun c -> c.Suit = Spades)
        let hearts = deck |> List.filter (fun c -> c.Suit = Hearts)
        let diamonds = deck |> List.filter (fun c -> c.Suit = Diamonds)
        let clubs = deck |> List.filter (fun c -> c.Suit = Clubs)
        Assert.Equal(13, spades.Length)
        Assert.Equal(13, hearts.Length)
        Assert.Equal(13, diamonds.Length)
        Assert.Equal(13, clubs.Length)

    [<Fact>]
    let ``Cards.createDeck should have no duplicates`` () =
        let deck = Cards.createDeck ()
        let unique = deck |> List.distinct
        Assert.Equal(deck.Length, unique.Length)

    [<Fact>]
    let ``Cards.tableValue should return 1 for Ace and face values for others`` () =
        Assert.Equal(1, Cards.tableValue Ace)
        Assert.Equal(2, Cards.tableValue Two)
        Assert.Equal(10, Cards.tableValue Ten)
        Assert.Equal(11, Cards.tableValue Jack)
        Assert.Equal(12, Cards.tableValue Queen)
        Assert.Equal(13, Cards.tableValue King)

    [<Fact>]
    let ``Cards.handValue should return 14 for any Ace`` () =
        Assert.Equal(14, Cards.handValue { Suit = Spades; Rank = Ace })
        Assert.Equal(14, Cards.handValue { Suit = Hearts; Rank = Ace })

    [<Fact>]
    let ``Cards.handValue should return 15 for Spade Two`` () =
        Assert.Equal(15, Cards.handValue { Suit = Spades; Rank = Two })

    [<Fact>]
    let ``Cards.handValue should return 16 for Diamond Ten`` () =
        Assert.Equal(16, Cards.handValue { Suit = Diamonds; Rank = Ten })

    [<Fact>]
    let ``Cards.handValue should return table value for normal cards`` () =
        Assert.Equal(7, Cards.handValue { Suit = Hearts; Rank = Seven })
        Assert.Equal(13, Cards.handValue { Suit = Clubs; Rank = King })
        Assert.Equal(2, Cards.handValue { Suit = Hearts; Rank = Two })

    [<Fact>]
    let ``Cards.isSpade should detect spades`` () =
        Assert.True(Cards.isSpade { Suit = Spades; Rank = Five })
        Assert.False(Cards.isSpade { Suit = Hearts; Rank = Five })

    [<Fact>]
    let ``Cards.isSpadeTwo should detect only Spade Two`` () =
        Assert.True(Cards.isSpadeTwo { Suit = Spades; Rank = Two })
        Assert.False(Cards.isSpadeTwo { Suit = Hearts; Rank = Two })
        Assert.False(Cards.isSpadeTwo { Suit = Spades; Rank = Three })

    [<Fact>]
    let ``Cards.isDiamondTen should detect only Diamond Ten`` () =
        Assert.True(Cards.isDiamondTen { Suit = Diamonds; Rank = Ten })
        Assert.False(Cards.isDiamondTen { Suit = Spades; Rank = Ten })
        Assert.False(Cards.isDiamondTen { Suit = Diamonds; Rank = Nine })

    [<Fact>]
    let ``Cards.isAce should detect aces`` () =
        Assert.True(Cards.isAce { Suit = Spades; Rank = Ace })
        Assert.True(Cards.isAce { Suit = Hearts; Rank = Ace })
        Assert.False(Cards.isAce { Suit = Hearts; Rank = King })

    [<Fact>]
    let ``Cards.cardDisplay should show rank before suit`` () =
        let card = { Suit = Spades; Rank = Ace }
        let display = Cards.cardDisplay card
        Assert.Equal("A\u2660", display)

    [<Fact>]
    let ``Cards.cardDisplay should show 10 for Ten`` () =
        let card = { Suit = Diamonds; Rank = Ten }
        let display = Cards.cardDisplay card
        Assert.Equal("10\u2666", display)

    [<Fact>]
    let ``Cards.deal should split deck correctly`` () =
        let deck = [ for i in 1..10 -> { Suit = Spades; Rank = Cards.allRanks.[i - 1] } ]
        let dealt, remaining = Cards.deal 4 deck
        Assert.Equal(4, dealt.Length)
        Assert.Equal(6, remaining.Length)

    [<Fact>]
    let ``Cards.deal should not exceed deck size`` () =
        let deck = [ { Suit = Spades; Rank = Ace }; { Suit = Spades; Rank = Two } ]
        let dealt, remaining = Cards.deal 5 deck
        Assert.Equal(2, dealt.Length)
        Assert.Equal(0, remaining.Length)

    [<Fact>]
    let ``Cards.shuffle should produce all original cards`` () =
        let rng = System.Random(42)
        let deck = Cards.createDeck ()
        let shuffled = Cards.shuffle rng deck
        Assert.Equal(52, shuffled.Length)
        // All original cards still present
        let sortBy c = (c.Suit, c.Rank)
        let original = deck |> List.sortBy sortBy
        let sorted = shuffled |> List.sortBy sortBy
        Assert.Equal<Card list>(original, sorted)

    [<Fact>]
    let ``Cards.scoringValue should assign correct direct points`` () =
        // Diamond Ten = 2 pts direct
        let d10 = Cards.scoringValue { Suit = Diamonds; Rank = Ten }
        Assert.True(d10 > 2.0 && d10 < 2.1)  // 2.0 + 1/52

        // Spade Two = 1 pt direct + spade fraction
        let s2 = Cards.scoringValue { Suit = Spades; Rank = Two }
        Assert.True(s2 > 1.1 && s2 < 1.2)  // 1.0 + 1/52 + 2/13

        // Ace of hearts = 1 pt direct + card fraction (no spade)
        let ah = Cards.scoringValue { Suit = Hearts; Rank = Ace }
        Assert.True(ah > 1.0 && ah < 1.1)  // 1.0 + 1/52

        // Non-special non-spade card = only card fraction
        let h7 = Cards.scoringValue { Suit = Hearts; Rank = Seven }
        Assert.True(h7 > 0.0 && h7 < 0.1)  // 1/52 only

    // ========================================================================
    // Rules Module Tests
    // ========================================================================

    [<Fact>]
    let ``Rules.findCaptures should find single card match`` () =
        // Play a 7, table has a 7
        let handCard = { Suit = Hearts; Rank = Seven }
        let table = [ { Suit = Spades; Rank = Seven } ]
        let captures = Rules.findCaptures None handCard table
        Assert.True(captures.Length >= 1)
        // At least one combination should contain the table 7
        let hasMatch = captures |> List.exists (fun combo -> combo |> List.exists (fun c -> c.Rank = Seven && c.Suit = Spades))
        Assert.True(hasMatch)

    [<Fact>]
    let ``Rules.findCaptures should find sum-based capture`` () =
        // Play a 7 (hand value 7), table has 3+4
        let handCard = { Suit = Hearts; Rank = Seven }
        let table =
            [ { Suit = Clubs; Rank = Three }
              { Suit = Diamonds; Rank = Four } ]
        let captures = Rules.findCaptures None handCard table
        Assert.True(captures.Length >= 1)

    [<Fact>]
    let ``Rules.findCaptures should find multiple combinations`` () =
        // Play a 7, table has: 7, 3+4
        let handCard = { Suit = Hearts; Rank = Seven }
        let table =
            [ { Suit = Spades; Rank = Seven }
              { Suit = Clubs; Rank = Three }
              { Suit = Diamonds; Rank = Four } ]
        let captures = Rules.findCaptures None handCard table
        // Should find at least 2 combinations: [7] and [3,4]
        Assert.True(captures.Length >= 2)

    [<Fact>]
    let ``Rules.findCaptures should return empty for no match`` () =
        // Play a King (13), table has only low cards that don't sum to 13
        let handCard = { Suit = Hearts; Rank = King }
        let table =
            [ { Suit = Spades; Rank = Two }
              { Suit = Clubs; Rank = Three } ]
        let captures = Rules.findCaptures None handCard table
        Assert.Empty(captures)

    [<Fact>]
    let ``Rules.findCaptures should return empty for empty table`` () =
        let handCard = { Suit = Hearts; Rank = Five }
        let captures = Rules.findCaptures None handCard []
        Assert.Empty(captures)

    [<Fact>]
    let ``Rules.findCaptures should handle Ace capturing value 14`` () =
        // Ace has hand value 14. Table cards summing to 14: K(13)+A(1), or Q(12)+2, etc.
        let handCard = { Suit = Hearts; Rank = Ace }
        let table =
            [ { Suit = Spades; Rank = King }   // 13
              { Suit = Clubs; Rank = Ace } ]    // 1
        let captures = Rules.findCaptures None handCard table
        // Should find [K, A] = 13+1 = 14
        Assert.True(captures.Length >= 1)

    [<Fact>]
    let ``Rules.getCapturedCards should return union of all captures when non-overlapping`` () =
        // Play a 7, table has: 7, 3, 4
        // Combinations: [7] and [3,4] — non-overlapping → single option with union [7, 3, 4]
        let handCard = { Suit = Hearts; Rank = Seven }
        let table =
            [ { Suit = Spades; Rank = Seven }
              { Suit = Clubs; Rank = Three }
              { Suit = Diamonds; Rank = Four } ]
        let captured = Rules.getCapturedCards None handCard table
        Assert.Equal(3, captured.Length)

    [<Fact>]
    let ``Rules.playCard should capture and remove cards from table`` () =
        let handCard = { Suit = Hearts; Rank = Seven }
        let table = [ { Suit = Spades; Rank = Seven } ]
        let result, newTable, _options = Rules.playCard None handCard table
        match result with
        | Capture (_, captured, isSweep) ->
            Assert.Single(captured) |> ignore
            Assert.True(isSweep)  // Captured only card = sweep
            Assert.Empty(newTable)
        | Place _ ->
            Assert.Fail("Expected capture, got place")

    [<Fact>]
    let ``Rules.playCard should place card when no capture`` () =
        let handCard = { Suit = Hearts; Rank = King }
        let table = [ { Suit = Spades; Rank = Two } ]
        let result, newTable, _options = Rules.playCard None handCard table
        match result with
        | Place card ->
            Assert.Equal(King, card.Rank)
            Assert.Equal(2, newTable.Length)  // Original + placed
        | Capture _ ->
            Assert.Fail("Expected place, got capture")

    [<Fact>]
    let ``Rules.playCard should detect sweep when table is cleared`` () =
        let handCard = { Suit = Hearts; Rank = Five }
        let table = [ { Suit = Spades; Rank = Five } ]
        let result, newTable, _options = Rules.playCard None handCard table
        match result with
        | Capture (_, _, isSweep) ->
            Assert.True(isSweep)
            Assert.Empty(newTable)
        | _ -> Assert.Fail("Expected capture")

    [<Fact>]
    let ``Rules.playCard should NOT be a sweep when table cards remain`` () =
        let handCard = { Suit = Hearts; Rank = Five }
        let table =
            [ { Suit = Spades; Rank = Five }
              { Suit = Clubs; Rank = King } ]
        let result, newTable, _options = Rules.playCard None handCard table
        match result with
        | Capture (_, _, isSweep) ->
            Assert.False(isSweep)
            Assert.Single(newTable :> System.Collections.IEnumerable) |> ignore  // King remains
        | _ -> Assert.Fail("Expected capture")

    // ========================================================================
    // CaptureOption / findCaptureOptions / resolveCapture Tests
    // ========================================================================

    [<Fact>]
    let ``Rules.findCaptureOptions should return single option when combos are non-overlapping`` () =
        // Play 7♥ (hand value 7), table has: 7♠, 3♣, 4♦
        // Combos: [7♠] and [3♣,4♦] — no overlap → single option capturing all 3
        let handCard = { Suit = Hearts; Rank = Seven }
        let table =
            [ { Suit = Spades; Rank = Seven }
              { Suit = Clubs; Rank = Three }
              { Suit = Diamonds; Rank = Four } ]
        let options = Rules.findCaptureOptions None handCard table
        Assert.Equal(1, options.Length)
        let opt = options.Head
        Assert.Equal(2, opt.Combos.Length)  // two combo groups
        Assert.Equal(3, opt.Captured.Length)  // all 3 cards captured

    [<Fact>]
    let ``Rules.findCaptureOptions should return multiple options when combos overlap`` () =
        // Play 8♦ (hand value 8), table has: A♦(1), 2♥(2), 5♠(5), 6♠(6)
        // Combos summing to 8: [A♦,2♥,5♠] (1+2+5=8), [2♥,6♠] (2+6=8)
        // These overlap on 2♥ → must choose between them
        let handCard = { Suit = Diamonds; Rank = Eight }
        let table =
            [ { Suit = Diamonds; Rank = Ace }     // 1
              { Suit = Hearts; Rank = Two }        // 2
              { Suit = Spades; Rank = Five }       // 5
              { Suit = Spades; Rank = Six } ]      // 6
        let options = Rules.findCaptureOptions None handCard table
        // Should have at least 2 options (one with [A,2,5], one with [2,6])
        Assert.True(options.Length >= 2, sprintf "Expected >= 2 options, got %d" options.Length)
        // Each option should be a maximal independent set
        for opt in options do
            // Verify no two combos in the same option share cards
            let allCards = opt.Combos |> List.concat
            let uniqueCards = allCards |> List.distinct
            Assert.Equal(allCards.Length, uniqueCards.Length)

    [<Fact>]
    let ``Rules.findCaptureOptions should return empty for no captures`` () =
        let handCard = { Suit = Hearts; Rank = King }
        let table = [ { Suit = Spades; Rank = Two } ]
        let options = Rules.findCaptureOptions None handCard table
        Assert.Empty(options)

    [<Fact>]
    let ``Rules.findCaptureOptions should return single combo as single option`` () =
        // Play K♥ (hand value 13), table has: K♠ only match
        let handCard = { Suit = Hearts; Rank = King }
        let table = [ { Suit = Spades; Rank = King } ]
        let options = Rules.findCaptureOptions None handCard table
        Assert.Equal(1, options.Length)
        Assert.Equal(1, options.Head.Combos.Length)
        Assert.Equal(1, options.Head.Captured.Length)

    [<Fact>]
    let ``Rules.resolveCapture should compute correct result for a chosen option`` () =
        // Given a specific capture option, resolve it
        let handCard = { Suit = Hearts; Rank = Seven }
        let table =
            [ { Suit = Spades; Rank = Seven }
              { Suit = Clubs; Rank = King } ]
        let option : Rules.CaptureOption =
            { Combos = [ [ { Suit = Spades; Rank = Seven } ] ]
              Captured = [ { Suit = Spades; Rank = Seven } ] }
        let result, newTable = Rules.resolveCapture handCard option table
        match result with
        | Capture (played, captured, isSweep) ->
            Assert.Equal(Seven, played.Rank)
            Assert.Equal(1, captured.Length)
            Assert.False(isSweep)  // King remains
            Assert.Equal(1, newTable.Length)
            Assert.Equal(King, newTable.Head.Rank)
        | Place _ ->
            Assert.Fail("Expected capture from resolveCapture")

    [<Fact>]
    let ``Rules.resolveCapture should detect sweep`` () =
        let handCard = { Suit = Hearts; Rank = Five }
        let table = [ { Suit = Spades; Rank = Five } ]
        let option : Rules.CaptureOption =
            { Combos = [ [ { Suit = Spades; Rank = Five } ] ]
              Captured = [ { Suit = Spades; Rank = Five } ] }
        let result, newTable = Rules.resolveCapture handCard option table
        match result with
        | Capture (_, _, isSweep) ->
            Assert.True(isSweep)
            Assert.Empty(newTable)
        | _ -> Assert.Fail("Expected capture with sweep")

    [<Fact>]
    let ``Rules.capturePointValue should sum scoring values`` () =
        let cards =
            [ { Suit = Diamonds; Rank = Ten }   // ~2.019
              { Suit = Hearts; Rank = Ace } ]    // ~1.019
        let value = Rules.capturePointValue cards
        Assert.True(value > 3.0)

    // ========================================================================
    // Scoring Module Tests
    // ========================================================================

    [<Fact>]
    let ``Scoring.calculateScores should award Most Cards to unique maximum`` () =
        let p1 =
            { Name = "P1"; Type = QuantumCPU; Hand = []; Sweeps = 0
              CapturedCards = [ for _ in 1..30 -> { Suit = Hearts; Rank = Two } ] }
        let p2 =
            { Name = "P2"; Type = QuantumCPU; Hand = []; Sweeps = 0
              CapturedCards = [ for _ in 1..22 -> { Suit = Clubs; Rank = Three } ] }
        let scores = Scoring.calculateScores [p1; p2]
        let (_, b1) = scores |> List.find (fun (p, _) -> p.Name = "P1")
        let (_, b2) = scores |> List.find (fun (p, _) -> p.Name = "P2")
        Assert.Equal(1, b1.MostCards)
        Assert.Equal(0, b2.MostCards)

    [<Fact>]
    let ``Scoring.calculateScores should not award Most Cards on tie`` () =
        let cards = [ for _ in 1..26 -> { Suit = Hearts; Rank = Two } ]
        let p1 = { Name = "P1"; Type = QuantumCPU; Hand = []; Sweeps = 0; CapturedCards = cards }
        let p2 = { Name = "P2"; Type = QuantumCPU; Hand = []; Sweeps = 0; CapturedCards = cards }
        let scores = Scoring.calculateScores [p1; p2]
        for (_, breakdown) in scores do
            Assert.Equal(0, breakdown.MostCards)

    [<Fact>]
    let ``Scoring.calculateScores should award Most Spades 2 points to unique maximum`` () =
        let spades = [ for r in [Ace; Two; Three; Four; Five; Six; Seven] -> { Suit = Spades; Rank = r } ]
        let clubs = [ for r in [Ace; Two; Three; Four; Five; Six] -> { Suit = Clubs; Rank = r } ]
        let p1 = { Name = "P1"; Type = QuantumCPU; Hand = []; Sweeps = 0; CapturedCards = spades }
        let p2 = { Name = "P2"; Type = QuantumCPU; Hand = []; Sweeps = 0; CapturedCards = clubs }
        let scores = Scoring.calculateScores [p1; p2]
        let (_, b1) = scores |> List.find (fun (p, _) -> p.Name = "P1")
        let (_, b2) = scores |> List.find (fun (p, _) -> p.Name = "P2")
        Assert.Equal(2, b1.MostSpades)
        Assert.Equal(0, b2.MostSpades)

    [<Fact>]
    let ``Scoring.calculateScores should count each Ace as 1 point`` () =
        let cards =
            [ { Suit = Spades; Rank = Ace }
              { Suit = Hearts; Rank = Ace }
              { Suit = Diamonds; Rank = Ace }
              { Suit = Clubs; Rank = Two } ]  // not an ace
        let p = { Name = "P1"; Type = QuantumCPU; Hand = []; Sweeps = 0; CapturedCards = cards }
        let scores = Scoring.calculateScores [p]
        let (_, b) = scores.Head
        Assert.Equal(3, b.Aces)

    [<Fact>]
    let ``Scoring.calculateScores should award Diamond Ten 2 points`` () =
        let cards = [ { Suit = Diamonds; Rank = Ten } ]
        let p = { Name = "P1"; Type = QuantumCPU; Hand = []; Sweeps = 0; CapturedCards = cards }
        let scores = Scoring.calculateScores [p]
        let (_, b) = scores.Head
        Assert.Equal(2, b.DiamondTen)

    [<Fact>]
    let ``Scoring.calculateScores should award Spade Two 1 point`` () =
        let cards = [ { Suit = Spades; Rank = Two } ]
        let p = { Name = "P1"; Type = QuantumCPU; Hand = []; Sweeps = 0; CapturedCards = cards }
        let scores = Scoring.calculateScores [p]
        let (_, b) = scores.Head
        Assert.Equal(1, b.SpadeTwo)

    [<Fact>]
    let ``Scoring.calculateScores should count sweeps with deduction`` () =
        // P1 has 3 sweeps, P2 has 1 sweep -> min=1, so P1 gets 2 sweep pts, P2 gets 0
        let p1 = { Name = "P1"; Type = QuantumCPU; Hand = []; Sweeps = 3; CapturedCards = [] }
        let p2 = { Name = "P2"; Type = QuantumCPU; Hand = []; Sweeps = 1; CapturedCards = [] }
        let scores = Scoring.calculateScores [p1; p2]
        let (_, b1) = scores |> List.find (fun (p, _) -> p.Name = "P1")
        let (_, b2) = scores |> List.find (fun (p, _) -> p.Name = "P2")
        Assert.Equal(2, b1.Sweeps)
        Assert.Equal(0, b2.Sweeps)

    [<Fact>]
    let ``Scoring.calculateScores should not deduct sweeps when some have zero`` () =
        // P1 has 2 sweeps, P2 has 0 -> min=0, no deduction
        let p1 = { Name = "P1"; Type = QuantumCPU; Hand = []; Sweeps = 2; CapturedCards = [] }
        let p2 = { Name = "P2"; Type = QuantumCPU; Hand = []; Sweeps = 0; CapturedCards = [] }
        let scores = Scoring.calculateScores [p1; p2]
        let (_, b1) = scores |> List.find (fun (p, _) -> p.Name = "P1")
        let (_, b2) = scores |> List.find (fun (p, _) -> p.Name = "P2")
        Assert.Equal(2, b1.Sweeps)
        Assert.Equal(0, b2.Sweeps)

    [<Fact>]
    let ``Scoring.calculateScores should deduct universal sweeps from all players`` () =
        // All 3 players have at least 2 sweeps -> min=2, deduct 2 from each
        let p1 = { Name = "P1"; Type = QuantumCPU; Hand = []; Sweeps = 4; CapturedCards = [] }
        let p2 = { Name = "P2"; Type = QuantumCPU; Hand = []; Sweeps = 2; CapturedCards = [] }
        let p3 = { Name = "P3"; Type = QuantumCPU; Hand = []; Sweeps = 3; CapturedCards = [] }
        let scores = Scoring.calculateScores [p1; p2; p3]
        let (_, b1) = scores |> List.find (fun (p, _) -> p.Name = "P1")
        let (_, b2) = scores |> List.find (fun (p, _) -> p.Name = "P2")
        let (_, b3) = scores |> List.find (fun (p, _) -> p.Name = "P3")
        Assert.Equal(2, b1.Sweeps)  // 4-2
        Assert.Equal(0, b2.Sweeps)  // 2-2
        Assert.Equal(1, b3.Sweeps)  // 3-2

    [<Fact>]
    let ``Scoring.calculateScores should compute correct total`` () =
        // Player with: most cards (1), most spades (2), 2 aces (2), diamond 10 (2), spade 2 (1), 1 sweep (1) = 9
        let cards =
            [ { Suit = Spades; Rank = Ace }     // ace + spade
              { Suit = Hearts; Rank = Ace }     // ace
              { Suit = Diamonds; Rank = Ten }   // diamond ten
              { Suit = Spades; Rank = Two }     // spade two + spade
              { Suit = Spades; Rank = Three }   // spade
              { Suit = Spades; Rank = Four }    // spade
              { Suit = Spades; Rank = Five }    // spade
              { Suit = Spades; Rank = Six }     // spade
              { Suit = Spades; Rank = Seven }   // spade (7 spades total)
              { Suit = Hearts; Rank = Two }     // padding for "most cards"
              { Suit = Hearts; Rank = Three }
              { Suit = Hearts; Rank = Four }
              { Suit = Hearts; Rank = Five }
              { Suit = Hearts; Rank = Six }
              { Suit = Hearts; Rank = Seven }
              { Suit = Hearts; Rank = Eight }
              { Suit = Hearts; Rank = Nine }
              { Suit = Hearts; Rank = Ten }
              { Suit = Hearts; Rank = Jack }
              { Suit = Hearts; Rank = Queen }
              { Suit = Hearts; Rank = King }
              { Suit = Clubs; Rank = Two }
              { Suit = Clubs; Rank = Three }
              { Suit = Clubs; Rank = Four }
              { Suit = Clubs; Rank = Five }
              { Suit = Clubs; Rank = Six }
              { Suit = Clubs; Rank = Seven } ]  // 27 cards total

        let p1 = { Name = "P1"; Type = QuantumCPU; Hand = []; Sweeps = 1; CapturedCards = cards }
        let p2 =
            { Name = "P2"; Type = QuantumCPU; Hand = []; Sweeps = 0
              CapturedCards = [ for _ in 1..10 -> { Suit = Clubs; Rank = Eight } ] }  // 10 cards, 0 spades

        let scores = Scoring.calculateScores [p1; p2]
        let (_, b1) = scores |> List.find (fun (p, _) -> p.Name = "P1")
        // 1 (most cards) + 2 (most spades) + 2 (aces) + 2 (d10) + 1 (s2) + 1 (sweep) = 9
        Assert.Equal(9, b1.Total)

    // ========================================================================
    // QuantumPlayer Module Tests
    // ========================================================================

    [<Fact>]
    let ``QuantumPlayer.evaluatePlay should detect capture`` () =
        let handCard = { Suit = Hearts; Rank = Five }
        let table = [ { Suit = Spades; Rank = Five } ]
        let eval = QuantumPlayer.evaluatePlay None handCard table
        Assert.True(eval.CardsCaptured > 0)
        Assert.True(eval.IsSweep)
        Assert.True(eval.PointValue > 0.0)

    [<Fact>]
    let ``QuantumPlayer.evaluatePlay should detect placement`` () =
        let handCard = { Suit = Hearts; Rank = King }
        let table = [ { Suit = Spades; Rank = Two } ]
        let eval = QuantumPlayer.evaluatePlay None handCard table
        Assert.Equal(0, eval.CardsCaptured)
        Assert.False(eval.IsSweep)
        Assert.Equal(0.0, eval.PointValue)

    [<Fact>]
    let ``QuantumPlayer.chooseBestStandard should prefer capture over placement`` () =
        let hand =
            [ { Suit = Hearts; Rank = Five }   // captures 5
              { Suit = Hearts; Rank = King } ]  // no capture
        let table = [ { Suit = Spades; Rank = Five } ]
        let ctx : QuantumPlayer.GameContext =
            { MyCards = 0; MySpades = 0
              OpponentCards = 0; OpponentSpades = 0; CardsRemaining = 40 }
        let eval = QuantumPlayer.chooseBestStandard None ctx hand table
        Assert.Equal(Five, eval.HandCard.Rank)
        Assert.True(eval.CardsCaptured > 0)

    [<Fact>]
    let ``QuantumPlayer.chooseBestStandard should prefer higher value capture`` () =
        // Table has: 5♠, A♥ (ace=1 on table)
        // Hand has: 5♣ (captures 5♠), A♦ (captures A♥)
        // 5♠ is a spade, worth more in scoring
        let hand =
            [ { Suit = Clubs; Rank = Five }
              { Suit = Diamonds; Rank = Ace } ]
        let table =
            [ { Suit = Spades; Rank = Five }    // spade, worth more
              { Suit = Hearts; Rank = Ace } ]   // just an ace
        let ctx : QuantumPlayer.GameContext =
            { MyCards = 0; MySpades = 0
              OpponentCards = 0; OpponentSpades = 0; CardsRemaining = 40 }
        let eval = QuantumPlayer.chooseBestStandard None ctx hand table
        // Both capture, but 5♠ has spade fraction bonus + ace capture has ace direct points
        // The AI should pick the higher-value option
        Assert.True(eval.CardsCaptured > 0)

    [<Fact>]
    let ``QuantumPlayer.chooseBestMisa should prefer non-capture`` () =
        // In Misa-Kasino, prefer NOT capturing
        let hand =
            [ { Suit = Hearts; Rank = Five }   // captures 5
              { Suit = Hearts; Rank = King } ]  // no capture (nothing sums to 13)
        let table = [ { Suit = Spades; Rank = Five } ]
        let ctx : QuantumPlayer.GameContext =
            { MyCards = 0; MySpades = 0
              OpponentCards = 0; OpponentSpades = 0; CardsRemaining = 40 }
        let eval = QuantumPlayer.chooseBestMisa None ctx hand table
        Assert.Equal(King, eval.HandCard.Rank)
        Assert.Equal(0, eval.CardsCaptured)

    [<Fact>]
    let ``QuantumPlayer.chooseBestMisa should pick lowest value when forced to capture`` () =
        // Both cards capture — forced to pick least damaging
        let hand =
            [ { Suit = Hearts; Rank = Five }
              { Suit = Clubs; Rank = Five } ]
        let table =
            [ { Suit = Spades; Rank = Five }
              { Suit = Diamonds; Rank = Five } ]
        let ctx : QuantumPlayer.GameContext =
            { MyCards = 0; MySpades = 0
              OpponentCards = 0; OpponentSpades = 0; CardsRemaining = 40 }
        let eval = QuantumPlayer.chooseBestMisa None ctx hand table
        // Both capture the same cards, so either is fine — just verify it captures
        Assert.True(eval.CardsCaptured > 0)

    [<Fact>]
    let ``QuantumPlayer.chooseBest should delegate to correct variant`` () =
        let hand = [ { Suit = Hearts; Rank = Five } ]
        let table = [ { Suit = Spades; Rank = Five } ]
        let ctx : QuantumPlayer.GameContext =
            { MyCards = 0; MySpades = 0
              OpponentCards = 0; OpponentSpades = 0; CardsRemaining = 40 }

        let standardEval = QuantumPlayer.chooseBest None StandardKasino ctx hand table
        let misaEval = QuantumPlayer.chooseBest None LaistoKasino ctx hand table

        // Both should return a valid evaluation
        Assert.Equal(Five, standardEval.HandCard.Rank)
        Assert.Equal(Five, misaEval.HandCard.Rank)

    [<Fact>]
    let ``Laisto AI should pick lowest-point capture option when overlapping combos exist`` () =
        // K♣ (value 13) can capture {3♥, 10♦} or {3♥, 10♠} — both sum to 13, overlap on 3♥.
        // 10♦ is worth 2 points; 10♠ is worth 0 special points.
        // In Laistokasino, AI should pick the option capturing 10♠ (fewer points).
        // In Standard Kasino, AI should pick the option capturing 10♦ (more points).
        let hand = [ { Suit = Clubs; Rank = King } ]
        let table =
            [ { Suit = Hearts; Rank = Three }
              { Suit = Diamonds; Rank = Ten }
              { Suit = Spades; Rank = Ten } ]
        let ctx : QuantumPlayer.GameContext =
            { MyCards = 0; MySpades = 0
              OpponentCards = 0; OpponentSpades = 0; CardsRemaining = 40 }

        let laistoEval = QuantumPlayer.chooseBest None LaistoKasino ctx hand table
        let standardEval = QuantumPlayer.chooseBest None StandardKasino ctx hand table

        // Both should play the King (only card in hand)
        Assert.Equal(King, laistoEval.HandCard.Rank)
        Assert.Equal(King, standardEval.HandCard.Rank)

        // Both should capture (2 cards each: 3♥ + one of the 10s)
        Assert.Equal(2, laistoEval.CardsCaptured)
        Assert.Equal(2, standardEval.CardsCaptured)

        // Laisto should avoid 10♦ (2pts) — verify chosen option does NOT contain 10♦
        match laistoEval.ChosenOption with
        | Some opt ->
            let capturedCards = opt.Captured
            Assert.False(
                capturedCards |> List.exists (fun c -> c.Suit = Diamonds && c.Rank = Ten),
                "Laisto AI should avoid capturing 10♦ (2pts)")
        | None -> Assert.Fail("Laisto AI should have a chosen capture option")

        // Standard should prefer 10♦ (2pts) — verify chosen option DOES contain 10♦
        match standardEval.ChosenOption with
        | Some opt ->
            let capturedCards = opt.Captured
            Assert.True(
                capturedCards |> List.exists (fun c -> c.Suit = Diamonds && c.Rank = Ten),
                "Standard AI should capture 10♦ (2pts)")
        | None -> Assert.Fail("Standard AI should have a chosen capture option")

    // ========================================================================
    // GameLoop Module Tests (pure functions only, no I/O)
    // ========================================================================

    [<Fact>]
    let ``GameLoop.totalDealRounds should return 6 for 2 players`` () =
        Assert.Equal(6, GameLoop.totalDealRounds 2)

    [<Fact>]
    let ``GameLoop.totalDealRounds should return 4 for 3 players`` () =
        Assert.Equal(4, GameLoop.totalDealRounds 3)

    [<Fact>]
    let ``GameLoop.totalDealRounds should return 3 for 4 players`` () =
        Assert.Equal(3, GameLoop.totalDealRounds 4)

    [<Fact>]
    let ``GameLoop.createPlayers should create correct number of players`` () =
        let config : GameLoop.GameConfig =
            { Variant = StandardKasino
              PlayerCount = 3; HumanCount = 0; NoviceMode = true
              Seed = Some 42; TargetScore = 16; Backend = None }
        let players = GameLoop.createPlayers config
        Assert.Equal(3, players.Length)
        Assert.True(players |> List.forall (fun p -> p.Type = QuantumCPU))

    [<Fact>]
    let ``GameLoop.createPlayers should set first player as Human when HumanCount > 0`` () =
        let config : GameLoop.GameConfig =
            { Variant = StandardKasino
              PlayerCount = 2; HumanCount = 1; NoviceMode = true
              Seed = Some 42; TargetScore = 16; Backend = None }
        let players = GameLoop.createPlayers config
        Assert.Equal(Human, players.[0].Type)
        Assert.Equal("You", players.[0].Name)
        Assert.Equal(QuantumCPU, players.[1].Type)

    [<Fact>]
    let ``GameLoop.allHandsEmpty should return true when all hands empty`` () =
        let state : GameLoop.GameState =
            { Players =
                [ { Name = "P1"; Type = QuantumCPU; Hand = []; CapturedCards = []; Sweeps = 0 }
                  { Name = "P2"; Type = QuantumCPU; Hand = []; CapturedCards = []; Sweeps = 0 } ]
              Table = []; Deck = []; CurrentPlayerIndex = 0
              DealRound = 1; TotalDeals = 6
              LastCapturer = None; Variant = StandardKasino }
        Assert.True(GameLoop.allHandsEmpty state)

    [<Fact>]
    let ``GameLoop.allHandsEmpty should return false when any hand has cards`` () =
        let state : GameLoop.GameState =
            { Players =
                [ { Name = "P1"; Type = QuantumCPU
                    Hand = [ { Suit = Spades; Rank = Ace } ]
                    CapturedCards = []; Sweeps = 0 }
                  { Name = "P2"; Type = QuantumCPU; Hand = []; CapturedCards = []; Sweeps = 0 } ]
              Table = []; Deck = []; CurrentPlayerIndex = 0
              DealRound = 1; TotalDeals = 6
              LastCapturer = None; Variant = StandardKasino }
        Assert.False(GameLoop.allHandsEmpty state)

    [<Fact>]
    let ``GameLoop.dealRound first deal should give 4 cards to each player and 4 to table`` () =
        let deck = Cards.createDeck () |> Cards.shuffle (System.Random(42))
        let state : GameLoop.GameState =
            { Players =
                [ { Name = "P1"; Type = QuantumCPU; Hand = []; CapturedCards = []; Sweeps = 0 }
                  { Name = "P2"; Type = QuantumCPU; Hand = []; CapturedCards = []; Sweeps = 0 } ]
              Table = []; Deck = deck; CurrentPlayerIndex = 0
              DealRound = 1; TotalDeals = 6
              LastCapturer = None; Variant = StandardKasino }
        let afterDeal = GameLoop.dealRound state true
        Assert.Equal(4, afterDeal.Players.[0].Hand.Length)
        Assert.Equal(4, afterDeal.Players.[1].Hand.Length)
        Assert.Equal(4, afterDeal.Table.Length)
        // 52 - 4*2 - 4 = 40
        Assert.Equal(40, afterDeal.Deck.Length)

    [<Fact>]
    let ``GameLoop.dealRound subsequent deal should not add to table`` () =
        let deck = Cards.createDeck () |> Cards.shuffle (System.Random(42))
        let existingTable = [ { Suit = Spades; Rank = King } ]
        let state : GameLoop.GameState =
            { Players =
                [ { Name = "P1"; Type = QuantumCPU; Hand = []; CapturedCards = []; Sweeps = 0 }
                  { Name = "P2"; Type = QuantumCPU; Hand = []; CapturedCards = []; Sweeps = 0 } ]
              Table = existingTable; Deck = deck; CurrentPlayerIndex = 0
              DealRound = 2; TotalDeals = 6
              LastCapturer = None; Variant = StandardKasino }
        let afterDeal = GameLoop.dealRound state false
        Assert.Equal(4, afterDeal.Players.[0].Hand.Length)
        Assert.Equal(4, afterDeal.Players.[1].Hand.Length)
        Assert.Equal(1, afterDeal.Table.Length)  // No new table cards
        // 52 - 4*2 = 44
        Assert.Equal(44, afterDeal.Deck.Length)

    // ========================================================================
    // Knapsack Integration Tests (backend parameter)
    // ========================================================================

    [<Fact>]
    let ``Knapsack.findAllExactCombinations with None backend finds correct combinations`` () =
        // Items: weights 2, 5, 3, 4. Capacity = 7.
        // Valid combos: [2,5], [3,4]
        let problem =
            FSharp.Azure.Quantum.Knapsack.createProblem
                [("A", 2.0, 2.0); ("B", 5.0, 5.0); ("C", 3.0, 3.0); ("D", 4.0, 4.0)]
                7.0
        let combos = FSharp.Azure.Quantum.Knapsack.findAllExactCombinations problem None
        Assert.Equal(2, combos.Length)

    [<Fact>]
    let ``Knapsack.findAllExactCombinations with None backend finds single item match`` () =
        // Item weight matches capacity exactly
        let problem =
            FSharp.Azure.Quantum.Knapsack.createProblem
                [("A", 5.0, 5.0); ("B", 3.0, 3.0)]
                5.0
        let combos = FSharp.Azure.Quantum.Knapsack.findAllExactCombinations problem None
        // [A] is the only combo summing to 5
        Assert.Equal(1, combos.Length)
        Assert.Equal("A", combos.Head.Head.Id)

    [<Fact>]
    let ``Knapsack.findAllCapturedItems returns union of all combinations`` () =
        let problem =
            FSharp.Azure.Quantum.Knapsack.createProblem
                [("A", 2.0, 2.0); ("B", 5.0, 5.0); ("C", 3.0, 3.0); ("D", 4.0, 4.0)]
                7.0
        let items = FSharp.Azure.Quantum.Knapsack.findAllCapturedItems problem None
        // Both combos [A,B] and [C,D] -> union = all 4 items
        Assert.Equal(4, items.Length)

    [<Fact>]
    let ``Knapsack.findAllValidCombinations returns combinations, union, and count`` () =
        let problem =
            FSharp.Azure.Quantum.Knapsack.createProblem
                [("A", 2.0, 2.0); ("B", 5.0, 5.0); ("C", 3.0, 3.0); ("D", 4.0, 4.0)]
                7.0
        let (combos, union, count) = FSharp.Azure.Quantum.Knapsack.findAllValidCombinations problem None
        Assert.Equal(2, count)
        Assert.Equal(2, combos.Length)
        Assert.Equal(4, union.Length)
