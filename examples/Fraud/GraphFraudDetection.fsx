// ==============================================================================
// Graph-Based Fraud Detection using Quantum QAOA
// ==============================================================================
// Detects fraud rings and money laundering in transaction networks via quantum
// MaxCut (QAOA) community detection.  Generates a synthetic network with known
// patterns (layering, money mules, circular flows), partitions with MaxCut, then
// scores each account by graph features + pattern involvement.
//
// Usage:
//   dotnet fsi GraphFraudDetection.fsx
//   dotnet fsi GraphFraudDetection.fsx -- --help
//   dotnet fsi GraphFraudDetection.fsx -- --accounts FRAUD01,MULE01
//   dotnet fsi GraphFraudDetection.fsx -- --quiet --output results.json --csv risk.csv
//
// References:
//   [1] Weber et al., "Anti-Money Laundering in Bitcoin", KDD Workshop (2019).
//   [2] Negre et al., "Detecting Multiple Communities using Quantum Annealing",
//       PLoS ONE 15(2), e0227538 (2020).
//   [3] Wikipedia: Money_laundering
//       https://en.wikipedia.org/wiki/Money_laundering
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ==============================================================================
// CLI
// ==============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "GraphFraudDetection.fsx"
    "Graph-based fraud detection using quantum QAOA community detection."
    [ { Cli.OptionSpec.Name = "accounts"; Description = "Comma-separated account IDs to filter output (default: all)"; Default = Some "all" }
      { Cli.OptionSpec.Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output (flag)"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let accountFilter = Cli.getCommaSeparated "accounts" args

// ==============================================================================
// TYPES
// ==============================================================================

type AccountType =
    | Personal
    | Business
    | Exchange
    | MoneyService
    | Unknown

type Account =
    { Id: string
      Type: AccountType
      CreationDate: DateTime
      Country: string
      RiskScore: float option }

type TransactionType =
    | Transfer
    | Payment
    | Withdrawal
    | Deposit
    | Exchange

type Transaction =
    { From: string
      To: string
      Amount: float
      Timestamp: DateTime
      TransactionType: TransactionType }

type GraphFeatures =
    { AccountId: string
      Degree: int
      InDegree: int
      OutDegree: int
      ClusteringCoefficient: float
      PageRank: float
      TotalVolume: float
      AvgTransactionSize: float
      TransactionVelocity: float }

type FraudPattern =
    { Members: string list
      Confidence: float
      PatternType: string }

type AccountRisk =
    { AccountId: string
      RiskScore: float
      Reasons: string list
      Community: int
      HasQuantumFailure: bool }

// ==============================================================================
// SYNTHETIC TRANSACTION NETWORK
// ==============================================================================

let generateSampleNetwork () =
    let legitimateAccounts =
        [ { Id = "ACC001"; Type = Business; CreationDate = DateTime(2020, 1, 15); Country = "US"; RiskScore = Some 0.1 }
          { Id = "ACC002"; Type = Business; CreationDate = DateTime(2019, 6, 20); Country = "US"; RiskScore = Some 0.15 }
          { Id = "ACC003"; Type = Business; CreationDate = DateTime(2018, 3, 10); Country = "US"; RiskScore = Some 0.1 }
          { Id = "ACC004"; Type = Personal; CreationDate = DateTime(2021, 2, 5); Country = "US"; RiskScore = Some 0.05 }
          { Id = "ACC005"; Type = Business; CreationDate = DateTime(2017, 8, 1); Country = "UK"; RiskScore = Some 0.2 }
          { Id = "ACC006"; Type = Personal; CreationDate = DateTime(2020, 11, 30); Country = "UK"; RiskScore = Some 0.1 }
          { Id = "ACC007"; Type = Personal; CreationDate = DateTime(2019, 4, 15); Country = "UK"; RiskScore = Some 0.08 } ]

    let fraudRingAccounts =
        [ { Id = "FRAUD01"; Type = Personal; CreationDate = DateTime(2023, 10, 1); Country = "XX"; RiskScore = None }
          { Id = "FRAUD02"; Type = Personal; CreationDate = DateTime(2023, 10, 2); Country = "XX"; RiskScore = None }
          { Id = "FRAUD03"; Type = Personal; CreationDate = DateTime(2023, 10, 3); Country = "XX"; RiskScore = None }
          { Id = "FRAUD04"; Type = MoneyService; CreationDate = DateTime(2023, 9, 28); Country = "XX"; RiskScore = None } ]

    let muleAccounts =
        [ { Id = "MULE01"; Type = Personal; CreationDate = DateTime(2023, 8, 15); Country = "NG"; RiskScore = Some 0.6 }
          { Id = "VICTIM01"; Type = Personal; CreationDate = DateTime(2015, 3, 20); Country = "US"; RiskScore = Some 0.05 }
          { Id = "VICTIM02"; Type = Personal; CreationDate = DateTime(2018, 7, 10); Country = "CA"; RiskScore = Some 0.03 }
          { Id = "VICTIM03"; Type = Personal; CreationDate = DateTime(2016, 12, 5); Country = "UK"; RiskScore = Some 0.04 } ]

    let allAccounts = legitimateAccounts @ fraudRingAccounts @ muleAccounts

    let legitimateTransactions =
        [ { From = "ACC001"; To = "ACC002"; Amount = 15000.0; Timestamp = DateTime(2024, 1, 5); TransactionType = Payment }
          { From = "ACC002"; To = "ACC003"; Amount = 12000.0; Timestamp = DateTime(2024, 1, 6); TransactionType = Payment }
          { From = "ACC003"; To = "ACC001"; Amount = 8000.0; Timestamp = DateTime(2024, 1, 7); TransactionType = Payment }
          { From = "ACC001"; To = "ACC004"; Amount = 3500.0; Timestamp = DateTime(2024, 1, 8); TransactionType = Transfer }
          { From = "ACC005"; To = "ACC006"; Amount = 2500.0; Timestamp = DateTime(2024, 1, 3); TransactionType = Payment }
          { From = "ACC006"; To = "ACC007"; Amount = 800.0; Timestamp = DateTime(2024, 1, 4); TransactionType = Transfer }
          { From = "ACC007"; To = "ACC005"; Amount = 1200.0; Timestamp = DateTime(2024, 1, 5); TransactionType = Payment }
          { From = "ACC002"; To = "ACC005"; Amount = 5000.0; Timestamp = DateTime(2024, 1, 10); TransactionType = Payment } ]

    let fraudRingTransactions =
        [ { From = "FRAUD01"; To = "FRAUD02"; Amount = 9800.0; Timestamp = DateTime(2024, 1, 15, 10, 0, 0); TransactionType = Transfer }
          { From = "FRAUD02"; To = "FRAUD03"; Amount = 9500.0; Timestamp = DateTime(2024, 1, 15, 10, 5, 0); TransactionType = Transfer }
          { From = "FRAUD03"; To = "FRAUD04"; Amount = 9200.0; Timestamp = DateTime(2024, 1, 15, 10, 10, 0); TransactionType = Transfer }
          { From = "FRAUD04"; To = "FRAUD01"; Amount = 4000.0; Timestamp = DateTime(2024, 1, 15, 10, 15, 0); TransactionType = Transfer }
          { From = "FRAUD01"; To = "FRAUD03"; Amount = 5000.0; Timestamp = DateTime(2024, 1, 16, 14, 0, 0); TransactionType = Transfer }
          { From = "FRAUD02"; To = "FRAUD04"; Amount = 4800.0; Timestamp = DateTime(2024, 1, 16, 14, 5, 0); TransactionType = Transfer } ]

    let muleTransactions =
        [ { From = "VICTIM01"; To = "MULE01"; Amount = 2000.0; Timestamp = DateTime(2024, 1, 12, 9, 0, 0); TransactionType = Transfer }
          { From = "VICTIM02"; To = "MULE01"; Amount = 1800.0; Timestamp = DateTime(2024, 1, 12, 11, 0, 0); TransactionType = Transfer }
          { From = "VICTIM03"; To = "MULE01"; Amount = 2200.0; Timestamp = DateTime(2024, 1, 12, 14, 0, 0); TransactionType = Transfer }
          { From = "MULE01"; To = "FRAUD04"; Amount = 5500.0; Timestamp = DateTime(2024, 1, 13, 8, 0, 0); TransactionType = Withdrawal } ]

    let allTransactions = legitimateTransactions @ fraudRingTransactions @ muleTransactions
    (allAccounts, allTransactions)

// ==============================================================================
// GRAPH FEATURE EXTRACTION
// ==============================================================================

let extractGraphFeatures (accounts: Account list) (transactions: Transaction list) : GraphFeatures list =
    let outgoing = transactions |> List.groupBy (fun t -> t.From) |> Map.ofList
    let incoming = transactions |> List.groupBy (fun t -> t.To) |> Map.ofList

    let getNeighbors accountId =
        let outNeighbors =
            outgoing |> Map.tryFind accountId |> Option.defaultValue [] |> List.map (fun t -> t.To)
        let inNeighbors =
            incoming |> Map.tryFind accountId |> Option.defaultValue [] |> List.map (fun t -> t.From)
        Set.union (Set.ofList outNeighbors) (Set.ofList inNeighbors)

    let clusteringCoefficient accountId =
        let neighbors = getNeighbors accountId |> Set.toList
        if neighbors.Length < 2 then
            0.0
        else
            let possibleEdges = neighbors.Length * (neighbors.Length - 1) / 2
            let actualEdges =
                [ for i in 0 .. neighbors.Length - 2 do
                    for j in i + 1 .. neighbors.Length - 1 do
                        let n1, n2 = neighbors.[i], neighbors.[j]
                        let hasEdge =
                            transactions
                            |> List.exists (fun t ->
                                (t.From = n1 && t.To = n2) || (t.From = n2 && t.To = n1))
                        if hasEdge then yield 1 ]
                |> List.length
            float actualEdges / float possibleEdges

    let pageRankScores =
        let n = float accounts.Length
        let dampingFactor = 0.85
        accounts
        |> List.map (fun acc ->
            let inLinks =
                incoming |> Map.tryFind acc.Id |> Option.defaultValue [] |> List.length
            let score = (1.0 - dampingFactor) / n + dampingFactor * (float inLinks / n)
            (acc.Id, score))
        |> Map.ofList

    accounts
    |> List.map (fun acc ->
        let outTx = outgoing |> Map.tryFind acc.Id |> Option.defaultValue []
        let inTx = incoming |> Map.tryFind acc.Id |> Option.defaultValue []
        let allTx = outTx @ inTx
        let totalVolume = allTx |> List.sumBy (fun t -> t.Amount)
        let neighbors = getNeighbors acc.Id

        let velocity =
            if allTx.IsEmpty then
                0.0
            else
                let dates = allTx |> List.map (fun t -> t.Timestamp)
                let span = (List.max dates - List.min dates).TotalDays + 1.0
                float allTx.Length / span

        { AccountId = acc.Id
          Degree = Set.count neighbors
          InDegree = inTx.Length
          OutDegree = outTx.Length
          ClusteringCoefficient = clusteringCoefficient acc.Id
          PageRank = pageRankScores |> Map.tryFind acc.Id |> Option.defaultValue 0.0
          TotalVolume = totalVolume
          AvgTransactionSize = if allTx.IsEmpty then 0.0 else totalVolume / float allTx.Length
          TransactionVelocity = velocity })

// ==============================================================================
// QUANTUM COMMUNITY DETECTION (MaxCut)
// ==============================================================================

let quantumBackend : IQuantumBackend = LocalBackend() :> IQuantumBackend

let detectCommunities (accounts: Account list) (transactions: Transaction list) =
    if not quiet then printfn "  Running quantum community detection..."

    let maxVolume = transactions |> List.map (fun t -> t.Amount) |> List.max

    let edges =
        transactions
        |> List.groupBy (fun t -> if t.From < t.To then (t.From, t.To) else (t.To, t.From))
        |> List.map (fun ((a, b), txs) ->
            let totalWeight = txs |> List.sumBy (fun t -> t.Amount) |> fun v -> v / maxVolume
            (a, b, totalWeight))

    let vertices = accounts |> List.map (fun a -> a.Id)
    let problem = MaxCut.createProblem vertices edges

    match MaxCut.solve problem (Some quantumBackend) with
    | Ok solution ->
        if not quiet then
            printfn "    Community 1: %d members" solution.PartitionS.Length
            printfn "    Community 2: %d members" solution.PartitionT.Length
            printfn "    Cut value: %.2f" solution.CutValue

        let membership =
            [ for id in solution.PartitionS -> (id, 1)
              for id in solution.PartitionT -> (id, 2) ]
            |> Map.ofList
        Ok membership

    | Error err ->
        if not quiet then eprintfn "  Community detection failed: %s" err.Message
        Error err

// ==============================================================================
// FRAUD PATTERN DETECTION
// ==============================================================================

let detectFraudPatterns (transactions: Transaction list) (features: GraphFeatures list) : FraudPattern list =

    // Pattern 1: Layering (rapid sequential transfers within 30 min)
    let layeringPatterns =
        let sortedTx = transactions |> List.sortBy (fun t -> t.Timestamp)
        let chains, currentChain =
            sortedTx
            |> List.fold (fun (chains: Transaction list list, currentChain: Transaction list) tx ->
                match currentChain with
                | [] -> (chains, [ tx ])
                | lastTx :: _ ->
                    let timeDiff = (tx.Timestamp - lastTx.Timestamp).TotalMinutes
                    if timeDiff < 30.0 && lastTx.To = tx.From then
                        (chains, tx :: currentChain)
                    else if currentChain.Length >= 3 then
                        (currentChain :: chains, [ tx ])
                    else
                        (chains, [ tx ])) ([], [])
        let allChains =
            if currentChain.Length >= 3 then currentChain :: chains else chains
        allChains
        |> List.map (fun chain ->
            let members =
                chain |> List.collect (fun t -> [ t.From; t.To ]) |> List.distinct
            { Members = members
              Confidence = min 1.0 (float chain.Length / 5.0)
              PatternType = "Layering" })

    // Pattern 2: Money mule (star topology — many in, few out)
    let mulePatterns =
        features
        |> List.filter (fun f -> f.InDegree >= 3 && f.OutDegree <= 1 && f.TransactionVelocity > 2.0)
        |> List.map (fun f ->
            let senders =
                transactions
                |> List.filter (fun t -> t.To = f.AccountId)
                |> List.map (fun t -> t.From)
            { Members = f.AccountId :: senders
              Confidence = 0.8
              PatternType = sprintf "Money Mule (%s <- %d senders)" f.AccountId senders.Length })

    // Pattern 3: Circular transactions
    let circularPatterns =
        let adjacency =
            transactions
            |> List.groupBy (fun t -> t.From)
            |> List.map (fun (from, txs) -> from, txs |> List.map (fun t -> t.To))
            |> Map.ofList
        let allIds = features |> List.map (fun f -> f.AccountId)
        let rec dfs path visited current depth maxDepth =
            if depth > maxDepth then []
            elif current = List.head path && depth > 2 then [ path ]
            elif Set.contains current visited then []
            else
                match Map.tryFind current adjacency with
                | None -> []
                | Some neighbors ->
                    neighbors
                    |> List.collect (fun next ->
                        dfs path (Set.add current visited) next (depth + 1) maxDepth)
        let cycles =
            allIds
            |> List.collect (fun start -> dfs [ start ] Set.empty start 0 5)
            |> List.filter (fun cycle -> cycle.Length >= 3)
            |> List.distinctBy (fun cycle -> cycle |> List.sort |> String.concat ",")
        cycles
        |> List.map (fun cycle ->
            { Members = cycle
              Confidence = 0.9
              PatternType = sprintf "Circular (%d accounts)" cycle.Length })

    layeringPatterns @ mulePatterns @ circularPatterns

// ==============================================================================
// RISK SCORING (functional — no mutable)
// ==============================================================================

let calculateRiskScores
    (accounts: Account list)
    (features: GraphFeatures list)
    (fraudPatterns: FraudPattern list)
    (communityMap: Map<string, int>)
    (hasQuantumFailure: bool)
    : AccountRisk list =

    let featureMap = features |> List.map (fun f -> f.AccountId, f) |> Map.ofList

    let fraudInvolvement =
        fraudPatterns
        |> List.collect (fun p ->
            p.Members |> List.map (fun m -> m, (p.Confidence, p.PatternType)))
        |> List.groupBy fst
        |> List.map (fun (acc, involvements) ->
            let maxConf = involvements |> List.map (snd >> fst) |> List.max
            let patterns = involvements |> List.map (snd >> snd) |> List.distinct
            (acc, (maxConf, patterns)))
        |> Map.ofList

    accounts
    |> List.map (fun acc ->
        let feature = featureMap |> Map.tryFind acc.Id
        let involvement = fraudInvolvement |> Map.tryFind acc.Id
        let community = communityMap |> Map.tryFind acc.Id |> Option.defaultValue 0

        // Accumulate (score, reasons) functionally
        let addIf cond points reason (s, rs) =
            if cond then (s + points, reason :: rs) else (s, rs)

        let score0, reasons0 = (0.0, [])

        // Factor 1: Graph structure anomalies
        let s1, r1 =
            match feature with
            | Some f ->
                (score0, reasons0)
                |> addIf (f.TransactionVelocity > 5.0) 0.2
                       (sprintf "High velocity: %.1f tx/day" f.TransactionVelocity)
                |> addIf (f.ClusteringCoefficient > 0.8) 0.15
                       "Highly clustered connections"
                |> addIf (f.InDegree > 5 && f.OutDegree <= 1) 0.25
                       "Money mule topology (many in, few out)"
            | None -> (score0, reasons0)

        // Factor 2: Account metadata
        let accountAge = (DateTime.Now - acc.CreationDate).TotalDays
        let s2, r2 =
            (s1, r1)
            |> addIf (acc.Country = "XX") 0.1 "Unknown jurisdiction"
            |> addIf (accountAge < 90.0) 0.15 (sprintf "New account (%.0f days old)" accountAge)

        // Factor 3: Fraud pattern involvement
        let s3, r3 =
            match involvement with
            | Some (confidence, patterns) ->
                let withPatterns = patterns |> List.fold (fun (s, rs) p -> (s, p :: rs)) (s2, r2)
                (fst withPatterns + confidence * 0.5, snd withPatterns)
            | None -> (s2, r2)

        // Factor 4: Pre-existing risk score
        let s4, r4 =
            match acc.RiskScore with
            | Some existing -> (s3 + existing * 0.2, r3)
            | None -> (s3 + 0.05, "No prior risk assessment" :: r3)

        { AccountId = acc.Id
          RiskScore = min 1.0 s4
          Reasons = List.rev r4
          Community = community
          HasQuantumFailure = hasQuantumFailure })
    |> List.filter (fun r -> r.RiskScore > 0.3)
    |> List.sortByDescending (fun r -> r.RiskScore)

// ==============================================================================
// MAIN ANALYSIS
// ==============================================================================

let (accounts, transactions) = generateSampleNetwork ()

if not quiet then
    printfn ""
    printfn "=================================================================="
    printfn "  Graph-Based Fraud Detection (Quantum QAOA)"
    printfn "=================================================================="
    printfn ""
    printfn "  Backend:       %s" quantumBackend.Name
    printfn "  Accounts:      %d" accounts.Length
    printfn "  Transactions:  %d" transactions.Length
    printfn "  Total Volume:  $%.2f" (transactions |> List.sumBy (fun t -> t.Amount))
    printfn ""

let features = extractGraphFeatures accounts transactions
let communityResult = detectCommunities accounts transactions
let fraudPatterns = detectFraudPatterns transactions features

let hasQuantumFailure =
    match communityResult with
    | Error _ -> true
    | Ok _ -> false

let communityMap =
    match communityResult with
    | Ok m -> m
    | Error _ -> Map.empty

let riskScores =
    calculateRiskScores accounts features fraudPatterns communityMap hasQuantumFailure

// Apply account filter
let filteredScores =
    match accountFilter with
    | [] -> riskScores
    | filters ->
        let filterSet = filters |> List.map (fun s -> s.ToUpperInvariant()) |> Set.ofList
        riskScores
        |> List.filter (fun r ->
            let key = r.AccountId.ToUpperInvariant()
            filterSet |> Set.exists (fun f -> key.Contains f))

// ==============================================================================
// RANKED COMPARISON TABLE
// ==============================================================================

let printTable () =
    printfn ""
    printfn "=================================================================="
    printfn "  Account Risk Scores (Quantum Graph Analysis)"
    printfn "=================================================================="
    printfn ""
    printfn "  %-12s  %8s  %9s  %4s  %-40s"
        "Account" "Risk%%" "Community" "Qtm?" "Top Reason"
    printfn "  %s" (String('=', 82))

    if List.isEmpty filteredScores then
        printfn "  (no high-risk accounts detected)"
    else
        filteredScores
        |> List.iter (fun r ->
            let topReason = r.Reasons |> List.tryHead |> Option.defaultValue "-"
            let truncated =
                if topReason.Length > 40 then topReason.[..39] else topReason
            printfn "  %-12s  %7.1f%%  %9d  %-4s  %s"
                r.AccountId (r.RiskScore * 100.0) r.Community
                (if r.HasQuantumFailure then "FAIL" else "OK")
                truncated)

    printfn ""

printTable ()

// ==============================================================================
// SUMMARY
// ==============================================================================

if not quiet then
    printfn "  Fraud patterns found:  %d" fraudPatterns.Length
    printfn "  High-risk accounts:    %d" filteredScores.Length
    printfn "  Quantum failures:      %s" (if hasQuantumFailure then "YES" else "none")

    if not (List.isEmpty fraudPatterns) then
        printfn ""
        for p in fraudPatterns do
            printfn "  [%3.0f%%] %s" (p.Confidence * 100.0) p.PatternType
            printfn "         Members: %s" (String.Join(", ", p.Members))
    printfn ""

// ==============================================================================
// STRUCTURED OUTPUT
// ==============================================================================

let resultMaps =
    filteredScores
    |> List.map (fun r ->
        [ "account_id", r.AccountId
          "risk_score", sprintf "%.4f" r.RiskScore
          "risk_pct", sprintf "%.1f" (r.RiskScore * 100.0)
          "community", string r.Community
          "top_reason", (r.Reasons |> List.tryHead |> Option.defaultValue "")
          "all_reasons", (r.Reasons |> String.concat "; ")
          "has_quantum_failure", string r.HasQuantumFailure ]
        |> Map.ofList)

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path resultMaps
    if not quiet then printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header =
        [ "account_id"; "risk_score"; "risk_pct"; "community"
          "top_reason"; "all_reasons"; "has_quantum_failure" ]
    let rows =
        resultMaps
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "CSV written to %s" path
| None -> ()

if argv.Length = 0 && not quiet then
    printfn ""
    printfn "Tip: Run with --help to see all options."
    printfn "     --accounts FRAUD01,MULE01  Filter by account ID"
    printfn "     --csv risk.csv             Export risk scores as CSV"
    printfn ""
