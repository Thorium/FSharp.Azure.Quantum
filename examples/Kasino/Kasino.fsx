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

// WIDER THAN THE BACKEND (FSharp.Azure.Quantum 1.5.1 and later):
//   A knapsack that needs more qubits than the backend runs is cut into blocks of items:
//   each block is asked for its best subset at every exact share of the capacity and the
//   answers are joined (QuboSplitting). A block needs no slack qubits, so 100 items run as
//   ten 10-qubit blocks; it takes one run per block and share instead of one run. The
//   solution's Split field reports the blocks and runs. Simulators split by default, a
//   backend that bills every circuit only with SplitPolicy.Always
//   (QaoaExecutionHelpers.SplitSettings). See the FAQ: "My problem is wider than the backend".

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
// The library comes from NuGet; `dotnet fsi --define:LOCAL_BUILD <script>` uses the repo's Debug build.
#if LOCAL_BUILD
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#else
#r "nuget: FSharp.Azure.Quantum"
#endif

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.Threading
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI ARGUMENT PARSING
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "Kasino.fsx"
    "Optimal card captures in the Finnish Kasino card game via quantum knapsack."
    [
        {
            Cli.OptionSpec.Name = "example"
            Description = "Scenario: simple|complex|strategy|sequence|all"
            Default = Some "simple"
        }
        {
            Cli.OptionSpec.Name = "output"
            Description = "Write results to JSON file"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "csv"
            Description = "Write results to CSV file"
            Default = None
        }
        {
            Cli.OptionSpec.Name = "quiet"
            Description = "Suppress informational output"
            Default = None
        }
    ]
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
    {
        Rank: Rank
        Value: float
        DisplayName: string
    }

/// Capture result with strategy analysis
type CaptureResult =
    {
        HandCard: Card
        CapturedCards: Card list
        TotalValue: float
        CardCount: int
        Strategy: string
        IsExactMatch: bool
    }

// ==============================================================================
// HELPER FUNCTIONS
// ==============================================================================

let rankValue =
    function
    | Ace -> 1.0
    | Number n -> float n
    | Jack -> 11.0
    | Queen -> 12.0
    | King -> 13.0

/// Capture power when the card is played from hand. In Kasino an Ace on the
/// table counts as 1, but played from hand it captures combos summing to 14.
/// (2♠=15 and 10♦=16 also exist, but this demo's cards carry no suit, so
/// those two specials are not representable here.)
let handValue =
    function
    | Ace -> 14.0
    | r -> rankValue r

let rankName =
    function
    | Ace -> "Ace"
    | Number n -> string n
    | Jack -> "Jack"
    | Queen -> "Queen"
    | King -> "King"

let card rank =
    {
        Rank = rank
        Value = rankValue rank
        DisplayName = rankName rank
    }

let displayCards cards =
    cards
    |> List.map (fun c -> $"%s{c.DisplayName}(%g{c.Value})")
    |> String.concat ", "

// ==============================================================================
// CAPTURE SOLVER - Uses Knapsack via IQuantumBackend (Rule 1)
// ==============================================================================

/// Local quantum simulator backend. Every capture search below runs iterative
/// QAOA on this backend. (Knapsack.solveAsync would fall back to an implicit local
/// quantum backend even for None, but passing it explicitly keeps the demo's
/// quantum-first intent visible in the code.)
let quantumBackend = Some(LocalBackendFactory.createUnified ())

/// Find optimal Kasino capture using Knapsack optimization.
/// Knapsack.solveAsync internally uses QAOA via IQuantumBackend.
let findOptimalCaptureAsync (handCard: Card) (tableCards: Card list) (strategy: string) =
    task {
        // The capture target is the card's HAND value (Ace = 14), while table
        // cards contribute their table values.
        let target = handValue handCard.Rank

        if not quiet then
            printfn "  Hand Card:   %s = %g (capture target)" handCard.DisplayName target
            printfn "  Table Cards: %s" (displayCards tableCards)
            printfn "  Strategy:    %s" strategy

        let items = tableCards |> List.map (fun c -> (c.DisplayName, c.Value, c.Value))

        let problem = Knapsack.createProblem items target

        match! Knapsack.solveAsync problem quantumBackend CancellationToken.None with
        | Ok solution ->
            let capturedCards =
                solution.SelectedItems
                |> List.choose (fun item -> tableCards |> List.tryFind (fun c -> c.DisplayName = item.Id))

            let result =
                {
                    HandCard = handCard
                    CapturedCards = capturedCards
                    TotalValue = solution.TotalValue
                    CardCount = capturedCards.Length
                    Strategy = strategy
                    IsExactMatch = abs (solution.TotalValue - target) < 1e-9
                }

            if not quiet then
                if result.IsExactMatch then
                    printfn "  Captured:    %s" (displayCards capturedCards)
                    printfn "  Total Value: %g / %g" result.TotalValue target
                    printfn "  Cards:       %d" result.CardCount
                    printfn "  EXACT MATCH - Perfect capture!"
                else
                    // Kasino captures must sum exactly — a lesser subset is not a
                    // legal capture, so the card would be placed on the table.
                    printfn "  Best subset: %s (= %g of %g)" (displayCards capturedCards) result.TotalValue target
                    printfn "  No exact match - the card is placed on the table."

                printfn ""

            return Some result

        | Error err ->
            if not quiet then
                printfn "  Solver error: %s" err.Message

            return None
    }

// ==============================================================================
// RESULT ROW BUILDER
// ==============================================================================

let resultRow (scenario: string) (result: CaptureResult) : Map<string, string> =
    Map.ofList
        [
            "scenario", scenario
            "hand_card", result.HandCard.DisplayName
            "hand_value", $"%g{result.HandCard.Value}"
            "captured", result.CapturedCards |> List.map (fun c -> c.DisplayName) |> String.concat "; "
            "total_value", $"%g{result.TotalValue}"
            "card_count", $"%d{result.CardCount}"
            "exact_match", $"%b{result.IsExactMatch}"
            "strategy", result.Strategy
        ]

// ==============================================================================
// BUILT-IN SCENARIOS
// ==============================================================================

let printHeader title =
    if not quiet then
        printfn ""
        printfn "%s" title
        printfn "%s" (String.replicate (String.length title) "-")

/// Result rows of one capture search (none when the solver failed).
let captureRowsAsync (scenario: string) (hand: Card) (table: Card list) (strategy: string) =
    task {
        let! capture = findOptimalCaptureAsync hand table strategy
        return capture |> Option.map (resultRow scenario) |> Option.toList
    }

/// Scenario 1: Simple capture — King vs small table
let runSimpleAsync () =
    task {
        printHeader "Scenario 1: Simple Capture (King vs Small Table)"
        let hand = card King
        let table = [ card (Number 2); card (Number 5); card (Number 8); card Jack ]

        return! captureRowsAsync "simple" hand table "Maximize value"
    }

/// Scenario 2: Complex capture — multiple optimal paths exist
let runComplexAsync () =
    task {
        printHeader "Scenario 2: Complex Capture (Multiple Solutions)"

        if not quiet then
            printfn "  Multiple subsets sum to 10: [4,6], [3,7], [1,2,3,4], ..."
            printfn ""

        let hand = card (Number 10)
        let table = [ 1..7 ] |> List.map (Number >> card)

        return! captureRowsAsync "complex" hand table "Maximize value"
    }

/// Scenario 3: Strategy comparison — same hand, same table, two perspectives
let runStrategyAsync () =
    task {
        printHeader "Scenario 3: Strategy Comparison"
        let hand = card Queen
        let table = [ card (Number 5); card (Number 7); card (Number 10); card (Number 3) ]

        if not quiet then
            printfn "  Strategy A: Maximize captured value"

        let! a = captureRowsAsync "strategy-maximize" hand table "Maximize value"

        if not quiet then
            printfn "  Strategy B: Same problem (minimize cards left for opponent)"

        let! b = captureRowsAsync "strategy-minimize" hand table "Minimize cards"

        return a @ b
    }

/// Scenario 4: Multi-turn game sequence
let runSequenceAsync () =
    task {
        printHeader "Scenario 4: Multi-Turn Game Sequence"

        let turns =
            [
                (card Ace, [ card King; card Ace ], "Turn 1: Ace (hand value 14) captures King + Ace (13 + 1)")
                (card (Number 7),
                 [ card (Number 2); card (Number 5); card (Number 3); card (Number 4) ],
                 "Turn 2: Multiple options")
                (card Queen, [ card (Number 5); card (Number 7); card (Number 10) ], "Turn 3: Strategic capture")
            ]

        let rows = ResizeArray()

        for (i, (hand, table, desc)) in List.indexed turns do
            if not quiet then
                printfn "  %s" desc

            let! turnRows =
                captureRowsAsync (sprintf "sequence-turn%d" (i + 1)) hand table "Maximize value"

            rows.AddRange turnRows

        return List.ofSeq rows
    }

// ==============================================================================
// MAIN EXECUTION
// ==============================================================================

if not quiet then
    printfn "======================================"
    printfn "Kasino - Finnish Card Game Optimizer"
    printfn "======================================"

let allResults = ResizeArray<Map<string, string>>()

let scenarios =
    match exampleName.ToLowerInvariant() with
    | "all" -> [ runSimpleAsync; runComplexAsync; runStrategyAsync; runSequenceAsync ]
    | "simple" -> [ runSimpleAsync ]
    | "complex" -> [ runComplexAsync ]
    | "strategy" -> [ runStrategyAsync ]
    | "sequence" -> [ runSequenceAsync ]
    | other ->
        eprintfn "Unknown example: '%s'. Use: simple|complex|strategy|sequence|all" other
        exit 1

task {
    for runScenarioAsync in scenarios do
        let! rows = runScenarioAsync ()
        allResults.AddRange rows
}
|> Async.AwaitTask
|> Async.RunSynchronously

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

    if not quiet then
        printfn "Results written to %s" path
| None -> ()

match csvPath with
| Some path ->
    let header =
        [
            "scenario"
            "hand_card"
            "hand_value"
            "captured"
            "total_value"
            "card_count"
            "exact_match"
            "strategy"
        ]

    let rows =
        resultRows
        |> List.map (fun m -> header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))

    Reporting.writeCsv path header rows

    if not quiet then
        printfn "Results written to %s" path
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
