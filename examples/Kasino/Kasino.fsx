/// Kasino Card Game - Finnish Traditional Game Example
///
/// USE CASE: Find optimal card captures using knapsack optimization in the
/// traditional Finnish card game Kasino.
///
/// PROBLEM: Given a hand card and table cards, find a subset of table cards
/// whose values sum exactly to the hand card value — maximizing captured value.
///
/// Kasino is a popular Finnish card game in the Nordic fishing-style family
/// (similar to Italian Scopa). Players capture table cards by matching their
/// sum to a hand card's value. This is a subset-sum problem — NP-complete —
/// naturally mapped to knapsack/QUBO optimization.

(*
===============================================================================
 Background Theory
===============================================================================

The capture step in Kasino reduces to a 0/1 Knapsack (or exact subset-sum)
instance: given table cards with values wᵢ and a hand card with value W,
find S ⊆ table cards maximizing Σᵢ∈S wᵢ subject to Σᵢ∈S wᵢ ≤ W. An exact
match (Σ = W) is a perfect capture.

For small tables (< 10 cards) classical DP is instantaneous, but the problem
structure illustrates quantum optimization well: the QUBO encoding places
binary variables xᵢ on each table card and penalises solutions exceeding
the hand card value while rewarding high total captured value.

Cultural Context:
  Kasino is part of Finnish cultural heritage — a family card game that
  teaches arithmetic, pattern recognition, and strategic thinking.

References:
  [1] Lucas, "Ising formulations of many NP problems", Front. Phys. 2 (2014).
  [2] Wikipedia: Casino (card game)
      https://en.wikipedia.org/wiki/Casino_(card_game)

Usage:
  dotnet fsi Kasino.fsx                                       (defaults)
  dotnet fsi Kasino.fsx -- --help                             (show options)
  dotnet fsi Kasino.fsx -- --example complex                  (specific scenario)
  dotnet fsi Kasino.fsx -- --quiet --output results.json      (pipeline mode)
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "Kasino.fsx"
    "Optimal card captures in the Finnish Kasino card game via quantum knapsack."
    [ { Cli.OptionSpec.Name = "example"; Description = "Scenario: simple|complex|strategy|sequence|all"; Default = Some "simple" }
      { Cli.OptionSpec.Name = "output";  Description = "Write results to JSON file";                     Default = None }
      { Cli.OptionSpec.Name = "csv";     Description = "Write results to CSV file";                      Default = None }
      { Cli.OptionSpec.Name = "quiet";   Description = "Suppress informational output";                  Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let exampleName = Cli.getOr "example" "simple" args

// ==============================================================================
// DOMAIN MODEL - Kasino Game Types
// ==============================================================================

/// Card rank in Kasino game
type Rank =
    | Ace
    | Number of int
    | Jack
    | Queen
    | King

/// Kasino card with rank, numeric value, and display name
type Card =
    { Rank: Rank
      Value: float
      DisplayName: string }

/// Capture result with strategy analysis
type CaptureResult =
    { HandCard: Card
      CapturedCards: Card list
      TotalValue: float
      CardCount: int
      Strategy: string
      IsExactMatch: bool }

// ==============================================================================
// HELPER FUNCTIONS
// ==============================================================================

let rankValue = function
    | Ace        -> 1.0
    | Number n   -> float n
    | Jack       -> 11.0
    | Queen      -> 12.0
    | King       -> 13.0

let rankName = function
    | Ace        -> "Ace"
    | Number n   -> string n
    | Jack       -> "Jack"
    | Queen      -> "Queen"
    | King       -> "King"

let card rank =
    { Rank = rank
      Value = rankValue rank
      DisplayName = rankName rank }

let displayCards cards =
    cards
    |> List.map (fun c -> sprintf "%s(%g)" c.DisplayName c.Value)
    |> String.concat ", "

// ==============================================================================
// CAPTURE SOLVER - Uses Knapsack via IQuantumBackend (Rule 1)
// ==============================================================================

/// Find optimal Kasino capture using Knapsack optimization.
/// Knapsack.solve internally uses QAOA via IQuantumBackend.
let findOptimalCapture (handCard: Card) (tableCards: Card list) (strategy: string) =
    if not quiet then
        printfn "  Hand Card:   %s = %g" handCard.DisplayName handCard.Value
        printfn "  Table Cards: %s" (displayCards tableCards)
        printfn "  Strategy:    %s" strategy

    let items =
        tableCards
        |> List.map (fun c -> (c.DisplayName, c.Value, c.Value))

    let problem = Knapsack.createProblem items handCard.Value

    match Knapsack.solve problem None with
    | Ok solution ->
        let capturedCards =
            solution.SelectedItems
            |> List.choose (fun item ->
                tableCards |> List.tryFind (fun c -> c.DisplayName = item.Id))

        let result =
            { HandCard = handCard
              CapturedCards = capturedCards
              TotalValue = solution.TotalValue
              CardCount = capturedCards.Length
              Strategy = strategy
              IsExactMatch = abs (solution.TotalValue - handCard.Value) < 1e-9 }

        if not quiet then
            printfn "  Captured:    %s" (displayCards capturedCards)
            printfn "  Total Value: %g / %g" result.TotalValue handCard.Value
            printfn "  Cards:       %d" result.CardCount
            if result.IsExactMatch then
                printfn "  EXACT MATCH - Perfect capture!"
            printfn ""

        Some result

    | Error err ->
        if not quiet then printfn "  Solver error: %A" err.Message
        None

// ==============================================================================
// RESULT ROW BUILDER
// ==============================================================================

let resultRow (scenario: string) (result: CaptureResult) : Map<string, string> =
    Map.ofList
        [ "scenario",     scenario
          "hand_card",    result.HandCard.DisplayName
          "hand_value",   sprintf "%g" result.HandCard.Value
          "captured",     result.CapturedCards |> List.map (fun c -> c.DisplayName) |> String.concat "; "
          "total_value",  sprintf "%g" result.TotalValue
          "card_count",   sprintf "%d" result.CardCount
          "exact_match",  sprintf "%b" result.IsExactMatch
          "strategy",     result.Strategy ]

// ==============================================================================
// BUILT-IN SCENARIOS
// ==============================================================================

let printHeader title =
    if not quiet then
        printfn ""
        printfn "%s" title
        printfn "%s" (String.replicate (String.length title) "-")

/// Scenario 1: Simple capture — King vs small table
let runSimple () =
    printHeader "Scenario 1: Simple Capture (King vs Small Table)"
    let hand = card King
    let table = [ card (Number 2); card (Number 5); card (Number 8); card Jack ]
    findOptimalCapture hand table "Maximize value"
    |> Option.map (resultRow "simple")

/// Scenario 2: Complex capture — multiple optimal paths exist
let runComplex () =
    printHeader "Scenario 2: Complex Capture (Multiple Solutions)"
    if not quiet then
        printfn "  Multiple subsets sum to 10: [4,6], [3,7], [1,2,3,4], ..."
        printfn ""
    let hand = card (Number 10)
    let table = [ 1 .. 7 ] |> List.map (fun n -> card (Number n))
    findOptimalCapture hand table "Maximize value"
    |> Option.map (resultRow "complex")

/// Scenario 3: Strategy comparison — same hand, same table, two perspectives
let runStrategy () =
    printHeader "Scenario 3: Strategy Comparison"
    let hand = card Queen
    let table =
        [ card (Number 5); card (Number 7); card (Number 10); card (Number 3) ]

    if not quiet then printfn "  Strategy A: Maximize captured value"
    let a =
        findOptimalCapture hand table "Maximize value"
        |> Option.map (resultRow "strategy-maximize")

    if not quiet then printfn "  Strategy B: Same problem (minimize cards left for opponent)"
    let b =
        findOptimalCapture hand table "Minimize cards"
        |> Option.map (resultRow "strategy-minimize")

    [ a; b ] |> List.choose id

/// Scenario 4: Multi-turn game sequence
let runSequence () =
    printHeader "Scenario 4: Multi-Turn Game Sequence"
    let turns =
        [ (card Ace,
           [ card Ace ],
           "Turn 1: Exact match")
          (card (Number 7),
           [ card (Number 2); card (Number 5); card (Number 3); card (Number 4) ],
           "Turn 2: Multiple options")
          (card Queen,
           [ card (Number 5); card (Number 7); card (Number 10) ],
           "Turn 3: Strategic capture") ]

    turns
    |> List.mapi (fun i (hand, table, desc) ->
        if not quiet then printfn "  %s" desc
        findOptimalCapture hand table "Maximize value"
        |> Option.map (resultRow (sprintf "sequence-turn%d" (i + 1))))
    |> List.choose id

// ==============================================================================
// MAIN EXECUTION
// ==============================================================================

if not quiet then
    printfn "======================================"
    printfn "Kasino - Finnish Card Game Optimizer"
    printfn "======================================"

let allResults = ResizeArray<Map<string, string>>()

match exampleName.ToLowerInvariant() with
| "all" ->
    runSimple ()   |> Option.iter allResults.Add
    runComplex ()  |> Option.iter allResults.Add
    runStrategy () |> List.iter allResults.Add
    runSequence () |> List.iter allResults.Add

| "simple"   -> runSimple ()   |> Option.iter allResults.Add
| "complex"  -> runComplex ()  |> Option.iter allResults.Add
| "strategy" -> runStrategy () |> List.iter allResults.Add
| "sequence" -> runSequence () |> List.iter allResults.Add
| other ->
    eprintfn "Unknown example: '%s'. Use: simple|complex|strategy|sequence|all" other
    exit 1

if not quiet then
    printfn "======================================"
    printfn "Kasino Examples Complete - Kiitos!"
    printfn "======================================"

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultRows = allResults |> Seq.toList

match outputPath with
| Some path ->
    Reporting.writeJson path resultRows
    if not quiet then printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [ "scenario"; "hand_card"; "hand_value"; "captured";
          "total_value"; "card_count"; "exact_match"; "strategy" ]
    let rows =
        resultRows
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

// ==============================================================================
// USAGE HINTS
// ==============================================================================

if argv.Length = 0 && not quiet then
    printfn ""
    printfn "Tip: Run with --help to see all options:"
    printfn "   dotnet fsi Kasino.fsx -- --help"
    printfn "   dotnet fsi Kasino.fsx -- --example all"
    printfn "   dotnet fsi Kasino.fsx -- --quiet --output results.json"
    printfn ""
