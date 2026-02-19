namespace FSharp.Azure.Quantum.Examples.Kasino

open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core

/// Rules engine for Kasino card game.
/// Uses the quantum Knapsack solver (iterative QAOA) to find all card captures.
/// Rule 1 compliant: all capture logic executes via IQuantumBackend.
module Rules =

    /// A capture option: one valid way to capture cards.
    /// Contains the list of non-overlapping combo groups and their union.
    type CaptureOption =
        { Combos: Card list list   // the individual non-overlapping combo groups
          Captured: Card list }     // union of all combos (the cards actually taken)

    /// Find all subsets of table cards that sum exactly to the hand card's value.
    /// Uses Knapsack.findAllExactCombinations with iterative QAOA via IQuantumBackend.
    let findCaptures (backend: BackendAbstraction.IQuantumBackend option) (handCard: Card) (tableCards: Card list) : Card list list =
        if List.isEmpty tableCards then
            []
        else
            let targetValue = Cards.handValue handCard

            // Build knapsack items: id encodes position to avoid duplicates
            let items =
                tableCards
                |> List.mapi (fun i c ->
                    let id = sprintf "%d_%s" i (Cards.cardDisplay c)
                    (id, float (Cards.tableValue c.Rank), float (Cards.tableValue c.Rank)))

            let problem = Knapsack.createProblem items (float targetValue)
            let combos = Knapsack.findAllExactCombinations problem backend

            // Map items back to original cards using positional index
            combos
            |> List.map (fun combo ->
                combo
                |> List.choose (fun item ->
                    match item.Id.Split('_') |> Array.tryHead with
                    | Some idxStr ->
                        match System.Int32.TryParse idxStr with
                        | true, idx when idx >= 0 && idx < tableCards.Length ->
                            Some tableCards.[idx]
                        | _ -> None
                    | None -> None))
            |> List.filter (fun cards -> not (List.isEmpty cards))

    /// Check whether two combos share any card (by structural equality).
    let private combosOverlap (a: Card list) (b: Card list) : bool =
        a |> List.exists (fun ca -> b |> List.exists (fun cb -> ca = cb))

    /// Find all maximal non-overlapping selections of combos.
    ///
    /// In Kasino, when multiple subsets of table cards each sum to the played
    /// card's value, you MUST capture all non-overlapping subsets simultaneously.
    /// When subsets overlap (share cards), you cannot use both — you must choose.
    ///
    /// A "capture option" is a maximal independent set: a set of combos where
    /// no two share any card, and no additional combo could be added without
    /// creating an overlap.
    ///
    /// Returns a list of CaptureOption, each representing one valid way to capture.
    /// If all combos are mutually non-overlapping, there is exactly one option
    /// containing all of them.
    let findCaptureOptions (backend: BackendAbstraction.IQuantumBackend option) (handCard: Card) (tableCards: Card list) : CaptureOption list =
        let allCombos = findCaptures backend handCard tableCards
        if List.isEmpty allCombos then
            []
        else
            // Build maximal independent sets via recursive backtracking.
            // For Kasino tables (typically 4-10 cards), combo count is small.
            let comboArr = allCombos |> Array.ofList
            let n = comboArr.Length

            // Precompute conflict matrix: conflicts.[i] = set of combo indices that overlap with i
            let conflicts =
                Array.init n (fun i ->
                    [ for j in 0 .. n - 1 do
                        if i <> j && combosOverlap comboArr.[i] comboArr.[j] then
                            yield j ]
                    |> Set.ofList)

            // Find all maximal independent sets using Bron-Kerbosch algorithm
            // on the complement graph (edges = non-conflicting pairs).
            // R = chosen set, P = candidates that can extend R, X = already processed.
            // A maximal clique in the complement = maximal independent set in the conflict graph.
            let results = System.Collections.Generic.List<int list>()

            let rec bronKerbosch (r: Set<int>) (p: Set<int>) (x: Set<int>) =
                if Set.isEmpty p && Set.isEmpty x then
                    // R is a maximal independent set
                    results.Add(r |> Set.toList)
                else
                    // Iterate over a snapshot of P
                    let pList = Set.toList p
                    let mutable pMut = p
                    let mutable xMut = x
                    for v in pList do
                        let neighbors = conflicts.[v]
                        // For independent sets: keep only candidates that DON'T conflict with v
                        let newP = pMut |> Set.remove v |> Set.filter (fun u -> not (neighbors.Contains u))
                        let newX = xMut |> Set.filter (fun u -> not (neighbors.Contains u))
                        bronKerbosch (Set.add v r) newP newX
                        pMut <- Set.remove v pMut
                        xMut <- Set.add v xMut

            bronKerbosch Set.empty (set [ 0 .. n - 1 ]) Set.empty

            // Deduplicate: two selections with same combo set (sorted) are identical
            results
            |> Seq.map (fun sel ->
                let sorted = sel |> List.sort
                let combos = sorted |> List.map (fun i -> comboArr.[i])
                let union = combos |> List.concat |> List.distinct
                (sorted, { Combos = combos; Captured = union }))
            |> Seq.distinctBy fst
            |> Seq.map snd
            |> Seq.toList

    /// Get all cards that would be captured when there is only one capture option
    /// (all combos are mutually non-overlapping). When multiple options exist,
    /// this returns the cards from the option capturing the most cards.
    /// For proper gameplay, use findCaptureOptions and let the player choose.
    let getCapturedCards (backend: BackendAbstraction.IQuantumBackend option) (handCard: Card) (tableCards: Card list) : Card list =
        let options = findCaptureOptions backend handCard tableCards
        match options with
        | [] -> []
        | [ single ] -> single.Captured
        | multiple ->
            // Fallback: pick the option with the most captured cards
            multiple
            |> List.sortByDescending (fun opt -> opt.Captured.Length)
            |> List.head
            |> fun opt -> opt.Captured

    /// Check if playing a card results in a capture
    let canCapture (backend: BackendAbstraction.IQuantumBackend option) (handCard: Card) (tableCards: Card list) : bool =
        not (List.isEmpty (findCaptures backend handCard tableCards))

    /// Determine the result of playing a hand card on the table.
    /// When there are multiple capture options (overlapping combos), returns
    /// the capture options list for the caller to resolve.
    /// Returns: PlayResult, new table state, and capture options (empty for Place).
    let playCard (backend: BackendAbstraction.IQuantumBackend option) (handCard: Card) (tableCards: Card list) : PlayResult * Card list * CaptureOption list =
        let options = findCaptureOptions backend handCard tableCards
        match options with
        | [] ->
            // No capture: place card on table
            let newTable = handCard :: tableCards
            (Place handCard, newTable, [])
        | [ single ] ->
            // Single option: auto-capture
            let captured = single.Captured
            let newTable =
                tableCards
                |> List.filter (fun c -> not (List.contains c captured))
            let isSweep = List.isEmpty newTable
            (Capture(handCard, captured, isSweep), newTable, options)
        | _ ->
            // Multiple options: return first option as default, but expose all options.
            // The caller (GameLoop) is responsible for letting the player/AI choose.
            // We use the first option's captured cards as a placeholder in the PlayResult.
            let first = options.Head
            let newTable =
                tableCards
                |> List.filter (fun c -> not (List.contains c first.Captured))
            let isSweep = List.isEmpty newTable
            (Capture(handCard, first.Captured, isSweep), newTable, options)

    /// Resolve a specific capture option: compute the PlayResult and new table.
    let resolveCapture (handCard: Card) (option: CaptureOption) (tableCards: Card list) : PlayResult * Card list =
        let captured = option.Captured
        let newTable =
            tableCards
            |> List.filter (fun c -> not (List.contains c captured))
        let isSweep = List.isEmpty newTable
        (Capture(handCard, captured, isSweep), newTable)

    /// In Misa-Kasino, a card "fits" on the table if it can capture something.
    /// A player must place a card (no capture) if possible. If ALL cards in hand
    /// can capture, the player must capture with the one yielding least points.
    let cardFitsTable (backend: BackendAbstraction.IQuantumBackend option) (handCard: Card) (tableCards: Card list) : bool =
        canCapture backend handCard tableCards

    /// Evaluate the point value of a set of captured cards (for scoring during play).
    /// Uses the card's scoringValue which combines direct points with fractional
    /// contributions toward "most cards" and "most spades" categories.
    let capturePointValue (captured: Card list) : float =
        captured |> List.sumBy Cards.scoringValue

    /// Calculate the point value of a sweep (clearing the table)
    let sweepBonus = 1.0
