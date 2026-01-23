/// Graph-Based Fraud Detection using Quantum QAOA
/// 
/// USE CASE: Detect fraud rings and money laundering networks
/// 
/// PROBLEM: Given a transaction network (accounts as nodes, transactions as edges),
/// identify suspicious clusters that may indicate coordinated fraud, money laundering,
/// or collusion rings.
/// 
/// This example demonstrates quantum graph analysis for financial crime detection,
/// inspired by classical Graph Attention Networks used for Bitcoin AML (Anti-Money Laundering).
/// 
/// Applications:
/// - Money laundering detection (layering through multiple accounts)
/// - Fraud ring identification (coordinated account takeovers)
/// - Collusion detection (vendor/employee kickback schemes)
/// - Synthetic identity fraud (connected fake accounts)

(*
===============================================================================
 Background Theory
===============================================================================

Graph-based fraud detection models financial transactions as a network where
accounts are vertices and transactions are edges. Fraudulent activity often
creates distinctive graph patterns: dense clusters (fraud rings), star topologies
(money mules), chains (layering), or anomalous community structures. Classical
approaches use Graph Neural Networks (GNNs), particularly Graph Attention Networks
(GATs) which learn to weight neighbor contributions, achieving state-of-the-art
results on datasets like Elliptic (Bitcoin transactions).

Quantum approaches offer potential advantages through QAOA-based community detection
and graph partitioning. The MaxCut formulation naturally identifies bipartite
structures (legitimate vs suspicious), while modularity optimization finds
densely connected clusters. For n accounts, the solution space has 2^n partitions;
quantum superposition explores these in parallel. Graph-based features (degree,
clustering coefficient, PageRank) combined with quantum kernel methods can detect
subtle structural anomalies invisible to feature-based classifiers alone.

Key Equations:
  - Modularity: Q = (1/2m) Σᵢⱼ [Aᵢⱼ - kᵢkⱼ/2m] δ(cᵢ,cⱼ)  where A=adjacency, k=degree
  - MaxCut formulation: C(z) = Σ₍ᵢ,ⱼ₎∈E wᵢⱼ·½(1 - zᵢzⱼ)  for partition z ∈ {±1}ⁿ
  - QAOA ansatz: |γ,β⟩ = Πₖ e^{-iβₖH_B} e^{-iγₖH_C} |+⟩ⁿ
  - Graph features: degree(v), clustering(v), PageRank(v), betweenness(v)
  - Suspicion score: S(v) = f(structural_features, community_membership, neighbor_labels)

Quantum Advantage:
  Graph fraud detection benefits from quantum computing in several ways:
  (1) Community detection via QAOA can identify fraud rings in exponentially
  large partition spaces. (2) Quantum walks provide efficient graph exploration
  for anomaly propagation. (3) Quantum kernels on graph embeddings capture
  higher-order structural patterns. For transaction networks with millions of
  edges, hybrid classical-quantum approaches use classical GNNs for feature
  extraction and quantum optimization for community structure analysis.
  Financial institutions exploring quantum graph analytics include Mastercard,
  PayPal, and major banks through IBM/Google partnerships.

References:
  [1] Weber et al., "Anti-Money Laundering in Bitcoin: Experimenting with Graph
      Convolutional Networks for Financial Forensics", KDD Workshop (2019).
      https://arxiv.org/abs/1908.02591
  [2] Pareja et al., "EvolveGCN: Evolving Graph Convolutional Networks for Dynamic
      Graphs", AAAI 2020. https://arxiv.org/abs/1902.10191
  [3] Negre et al., "Detecting Multiple Communities using Quantum Annealing on the
      D-Wave System", PLoS ONE 15(2), e0227538 (2020).
      https://doi.org/10.1371/journal.pone.0227538
  [4] Shaydulin et al., "A Hybrid Approach for Solving Optimization Problems on
      Small Quantum Computers", IEEE Computer 52(6), 18-26 (2019).
      https://doi.org/10.1109/MC.2019.2908942
  [5] Wikipedia: Money_laundering, Graph_neural_network
      https://en.wikipedia.org/wiki/Money_laundering
      https://en.wikipedia.org/wiki/Graph_neural_network
*)

//#r "nuget: FSharp.Azure.Quantum"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open FSharp.Azure.Quantum

// ============================================================================
// DOMAIN TYPES
// ============================================================================

/// Account in the transaction network
type Account = {
    Id: string
    Type: AccountType
    CreationDate: DateTime
    Country: string
    RiskScore: float option  // Pre-existing risk assessment if available
}

and AccountType =
    | Personal
    | Business
    | Exchange      // Crypto exchange
    | MoneyService  // MSB like Western Union
    | Unknown

/// Transaction edge in the network
type Transaction = {
    From: string
    To: string
    Amount: float
    Timestamp: DateTime
    TransactionType: TransactionType
}

and TransactionType =
    | Transfer
    | Payment
    | Withdrawal
    | Deposit
    | Exchange

/// Graph-based features for an account
type GraphFeatures = {
    AccountId: string
    Degree: int                    // Number of connections
    InDegree: int                  // Incoming transactions
    OutDegree: int                 // Outgoing transactions
    ClusteringCoefficient: float   // How connected neighbors are
    PageRank: float                // Importance in network
    BetweennessCentrality: float   // Bridge between communities
    TotalVolume: float             // Total transaction volume
    AvgTransactionSize: float      // Average transaction amount
    TransactionVelocity: float     // Transactions per day
}

/// Community detection result
type Community = {
    Id: int
    Members: string list
    Size: int
    InternalDensity: float         // Edge density within community
    ExternalConnections: int       // Edges to other communities
    SuspicionScore: float          // Aggregate risk score
    Characteristics: string list   // Detected patterns
}

/// Fraud detection result
type FraudAnalysisResult = {
    HighRiskAccounts: (string * float * string list) list  // (account, score, reasons)
    SuspiciousCommunities: Community list
    FraudRings: (string list * float * string) list        // (members, confidence, pattern)
    NetworkMetrics: NetworkMetrics
    Recommendations: string list
}

and NetworkMetrics = {
    TotalAccounts: int
    TotalTransactions: int
    TotalVolume: float
    AverageClusteringCoefficient: float
    NetworkDensity: float
    NumberOfCommunities: int
}

// ============================================================================
// SAMPLE DATA - Synthetic Transaction Network
// ============================================================================

/// Generate a synthetic transaction network with known fraud patterns
let generateSampleNetwork () =
    let random = Random(42)
    
    // Legitimate accounts (clustered by business relationships)
    let legitimateAccounts = [
        // Business cluster 1 - Supply chain
        { Id = "ACC001"; Type = Business; CreationDate = DateTime(2020, 1, 15); Country = "US"; RiskScore = Some 0.1 }
        { Id = "ACC002"; Type = Business; CreationDate = DateTime(2019, 6, 20); Country = "US"; RiskScore = Some 0.15 }
        { Id = "ACC003"; Type = Business; CreationDate = DateTime(2018, 3, 10); Country = "US"; RiskScore = Some 0.1 }
        { Id = "ACC004"; Type = Personal; CreationDate = DateTime(2021, 2, 5); Country = "US"; RiskScore = Some 0.05 }
        
        // Business cluster 2 - Retail
        { Id = "ACC005"; Type = Business; CreationDate = DateTime(2017, 8, 1); Country = "UK"; RiskScore = Some 0.2 }
        { Id = "ACC006"; Type = Personal; CreationDate = DateTime(2020, 11, 30); Country = "UK"; RiskScore = Some 0.1 }
        { Id = "ACC007"; Type = Personal; CreationDate = DateTime(2019, 4, 15); Country = "UK"; RiskScore = Some 0.08 }
    ]
    
    // Suspicious accounts (fraud ring pattern)
    let fraudRingAccounts = [
        // Fraud ring - layering pattern (money flows through multiple accounts)
        { Id = "FRAUD01"; Type = Personal; CreationDate = DateTime(2023, 10, 1); Country = "XX"; RiskScore = None }
        { Id = "FRAUD02"; Type = Personal; CreationDate = DateTime(2023, 10, 2); Country = "XX"; RiskScore = None }
        { Id = "FRAUD03"; Type = Personal; CreationDate = DateTime(2023, 10, 3); Country = "XX"; RiskScore = None }
        { Id = "FRAUD04"; Type = MoneyService; CreationDate = DateTime(2023, 9, 28); Country = "XX"; RiskScore = None }
    ]
    
    // Money mule pattern (star topology - one account receiving from many)
    let muleAccounts = [
        { Id = "MULE01"; Type = Personal; CreationDate = DateTime(2023, 8, 15); Country = "NG"; RiskScore = Some 0.6 }
        { Id = "VICTIM01"; Type = Personal; CreationDate = DateTime(2015, 3, 20); Country = "US"; RiskScore = Some 0.05 }
        { Id = "VICTIM02"; Type = Personal; CreationDate = DateTime(2018, 7, 10); Country = "CA"; RiskScore = Some 0.03 }
        { Id = "VICTIM03"; Type = Personal; CreationDate = DateTime(2016, 12, 5); Country = "UK"; RiskScore = Some 0.04 }
    ]
    
    let allAccounts = legitimateAccounts @ fraudRingAccounts @ muleAccounts
    
    // Legitimate transactions (normal business patterns)
    let legitimateTransactions = [
        // Supply chain cluster
        { From = "ACC001"; To = "ACC002"; Amount = 15000.0; Timestamp = DateTime(2024, 1, 5); TransactionType = Payment }
        { From = "ACC002"; To = "ACC003"; Amount = 12000.0; Timestamp = DateTime(2024, 1, 6); TransactionType = Payment }
        { From = "ACC003"; To = "ACC001"; Amount = 8000.0; Timestamp = DateTime(2024, 1, 7); TransactionType = Payment }
        { From = "ACC001"; To = "ACC004"; Amount = 3500.0; Timestamp = DateTime(2024, 1, 8); TransactionType = Transfer }
        
        // Retail cluster
        { From = "ACC005"; To = "ACC006"; Amount = 2500.0; Timestamp = DateTime(2024, 1, 3); TransactionType = Payment }
        { From = "ACC006"; To = "ACC007"; Amount = 800.0; Timestamp = DateTime(2024, 1, 4); TransactionType = Transfer }
        { From = "ACC007"; To = "ACC005"; Amount = 1200.0; Timestamp = DateTime(2024, 1, 5); TransactionType = Payment }
        
        // Cross-cluster (legitimate business)
        { From = "ACC002"; To = "ACC005"; Amount = 5000.0; Timestamp = DateTime(2024, 1, 10); TransactionType = Payment }
    ]
    
    // Fraud ring transactions (layering pattern - rapid sequential transfers)
    let fraudRingTransactions = [
        { From = "FRAUD01"; To = "FRAUD02"; Amount = 9800.0; Timestamp = DateTime(2024, 1, 15, 10, 0, 0); TransactionType = Transfer }
        { From = "FRAUD02"; To = "FRAUD03"; Amount = 9500.0; Timestamp = DateTime(2024, 1, 15, 10, 5, 0); TransactionType = Transfer }
        { From = "FRAUD03"; To = "FRAUD04"; Amount = 9200.0; Timestamp = DateTime(2024, 1, 15, 10, 10, 0); TransactionType = Transfer }
        { From = "FRAUD04"; To = "FRAUD01"; Amount = 4000.0; Timestamp = DateTime(2024, 1, 15, 10, 15, 0); TransactionType = Transfer }
        // Additional circular patterns
        { From = "FRAUD01"; To = "FRAUD03"; Amount = 5000.0; Timestamp = DateTime(2024, 1, 16, 14, 0, 0); TransactionType = Transfer }
        { From = "FRAUD02"; To = "FRAUD04"; Amount = 4800.0; Timestamp = DateTime(2024, 1, 16, 14, 5, 0); TransactionType = Transfer }
    ]
    
    // Money mule transactions (star pattern - many victims to one mule)
    let muleTransactions = [
        { From = "VICTIM01"; To = "MULE01"; Amount = 2000.0; Timestamp = DateTime(2024, 1, 12, 9, 0, 0); TransactionType = Transfer }
        { From = "VICTIM02"; To = "MULE01"; Amount = 1800.0; Timestamp = DateTime(2024, 1, 12, 11, 0, 0); TransactionType = Transfer }
        { From = "VICTIM03"; To = "MULE01"; Amount = 2200.0; Timestamp = DateTime(2024, 1, 12, 14, 0, 0); TransactionType = Transfer }
        // Mule cashing out
        { From = "MULE01"; To = "FRAUD04"; Amount = 5500.0; Timestamp = DateTime(2024, 1, 13, 8, 0, 0); TransactionType = Withdrawal }
    ]
    
    let allTransactions = legitimateTransactions @ fraudRingTransactions @ muleTransactions
    
    (allAccounts, allTransactions)

// ============================================================================
// GRAPH FEATURE EXTRACTION
// ============================================================================

/// Calculate graph features for each account
let extractGraphFeatures (accounts: Account list) (transactions: Transaction list) : GraphFeatures list =
    
    let accountIds = accounts |> List.map (_.Id) |> Set.ofList
    
    // Build adjacency information
    let outgoing = 
        transactions 
        |> List.groupBy (_.From) 
        |> Map.ofList
    
    let incoming = 
        transactions 
        |> List.groupBy (_.To) 
        |> Map.ofList
    
    // Get all neighbors for an account
    let getNeighbors accountId =
        let outNeighbors = 
            outgoing 
            |> Map.tryFind accountId 
            |> Option.defaultValue [] 
            |> List.map (_.To)
        let inNeighbors = 
            incoming 
            |> Map.tryFind accountId 
            |> Option.defaultValue [] 
            |> List.map (_.From)
        Set.union (Set.ofList outNeighbors) (Set.ofList inNeighbors)
    
    // Calculate clustering coefficient
    let clusteringCoefficient accountId =
        let neighbors = getNeighbors accountId |> Set.toList
        if neighbors.Length < 2 then 0.0
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
    
    // Simple PageRank approximation (one iteration for demo)
    let pageRankScores =
        let n = float accounts.Length
        let dampingFactor = 0.85
        accounts
        |> List.map (fun acc ->
            let inLinks = 
                incoming 
                |> Map.tryFind acc.Id 
                |> Option.defaultValue [] 
                |> List.length
            let score = (1.0 - dampingFactor) / n + dampingFactor * (float inLinks / n)
            (acc.Id, score))
        |> Map.ofList
    
    // Calculate features for each account
    accounts
    |> List.map (fun acc ->
        let outTx = outgoing |> Map.tryFind acc.Id |> Option.defaultValue []
        let inTx = incoming |> Map.tryFind acc.Id |> Option.defaultValue []
        let allTx = outTx @ inTx
        let totalVolume = allTx |> List.sumBy (_.Amount)
        let neighbors = getNeighbors acc.Id
        
        // Calculate transaction velocity (transactions per day)
        let velocity =
            if allTx.IsEmpty then 0.0
            else
                let dates = allTx |> List.map (_.Timestamp)
                let span = (List.max dates - List.min dates).TotalDays + 1.0
                float allTx.Length / span
        
        {
            AccountId = acc.Id
            Degree = Set.count neighbors
            InDegree = inTx.Length
            OutDegree = outTx.Length
            ClusteringCoefficient = clusteringCoefficient acc.Id
            PageRank = pageRankScores |> Map.tryFind acc.Id |> Option.defaultValue 0.0
            BetweennessCentrality = 0.0  // Simplified - would need full graph traversal
            TotalVolume = totalVolume
            AvgTransactionSize = if allTx.IsEmpty then 0.0 else totalVolume / float allTx.Length
            TransactionVelocity = velocity
        })

// ============================================================================
// QUANTUM COMMUNITY DETECTION USING MAXCUT
// ============================================================================

/// Detect communities using quantum MaxCut partitioning
let detectCommunities (accounts: Account list) (transactions: Transaction list) =
    printfn "  Running quantum community detection..."
    
    // Build weighted graph for MaxCut
    // Weight = transaction volume between accounts (normalized)
    let maxVolume = transactions |> List.map (_.Amount) |> List.max
    
    let edges = 
        transactions
        |> List.groupBy (fun t -> if t.From < t.To then (t.From, t.To) else (t.To, t.From))
        |> List.map (fun ((a, b), txs) -> 
            let totalWeight = txs |> List.sumBy (_.Amount) |> fun v -> v / maxVolume
            (a, b, totalWeight))
    
    let vertices = accounts |> List.map (_.Id)
    
    // Use MaxCut to partition the network
    // Accounts with strong connections will be grouped together
    let problem = MaxCut.createProblem vertices edges
    
    match MaxCut.solve problem None with
    | Ok solution ->
        // Convert MaxCut result to communities
        let community1 = {
            Id = 1
            Members = solution.PartitionS
            Size = solution.PartitionS.Length
            InternalDensity = 0.0  // Would calculate from edge counts
            ExternalConnections = solution.CutEdges.Length
            SuspicionScore = 0.0
            Characteristics = []
        }
        
        let community2 = {
            Id = 2
            Members = solution.PartitionT
            Size = solution.PartitionT.Length
            InternalDensity = 0.0
            ExternalConnections = solution.CutEdges.Length
            SuspicionScore = 0.0
            Characteristics = []
        }
        
        printfn "  Found %d communities via quantum partitioning" 2
        printfn "    Community 1: %d members" community1.Size
        printfn "    Community 2: %d members" community2.Size
        printfn "    Cut value (inter-community connections): %.2f" solution.CutValue
        
        Ok [community1; community2]
    
    | Error err ->
        printfn "  Community detection failed: %s" err.Message
        Error err

// ============================================================================
// FRAUD PATTERN DETECTION
// ============================================================================

/// Detect specific fraud patterns in the transaction network
let detectFraudPatterns (accounts: Account list) (transactions: Transaction list) (features: GraphFeatures list) =
    
    let featureMap = features |> List.map (fun f -> f.AccountId, f) |> Map.ofList
    
    // Pattern 1: Layering (rapid sequential transfers)
    let detectLayering () =
        printfn "  Checking for layering patterns..."
        
        // Find chains of rapid transfers (within 30 minutes)
        let sortedTx = transactions |> List.sortBy (_.Timestamp)
        
        let chains = 
            sortedTx
            |> List.fold (fun (chains, currentChain) tx ->
                match currentChain with
                | [] -> (chains, [tx])
                | lastTx :: _ ->
                    let timeDiff = (tx.Timestamp - lastTx.Timestamp).TotalMinutes
                    if timeDiff < 30.0 && lastTx.To = tx.From then
                        (chains, tx :: currentChain)
                    else
                        if currentChain.Length >= 3 then
                            (currentChain :: chains, [tx])
                        else
                            (chains, [tx])
            ) ([], [])
            |> fun (chains, current) -> 
                if current.Length >= 3 then current :: chains else chains
        
        chains
        |> List.map (fun chain ->
            let members = 
                chain 
                |> List.collect (fun t -> [t.From; t.To]) 
                |> List.distinct
            let confidence = min 1.0 (float chain.Length / 5.0)
            (members, confidence, "Layering: Rapid sequential transfers detected"))
    
    // Pattern 2: Money Mule (star topology - one receiver, many senders)
    let detectMoneyMules () =
        printfn "  Checking for money mule patterns..."
        
        features
        |> List.filter (fun f -> 
            // High in-degree, low out-degree, recent high velocity
            f.InDegree >= 3 && f.OutDegree <= 1 && f.TransactionVelocity > 2.0)
        |> List.map (fun f ->
            let senders = 
                transactions 
                |> List.filter (fun t -> t.To = f.AccountId) 
                |> List.map (_.From)
            ([f.AccountId] @ senders, 0.8, sprintf "Money Mule: %s receiving from %d accounts" f.AccountId senders.Length))
    
    // Pattern 3: Circular transactions (funds returning to origin)
    let detectCircular () =
        printfn "  Checking for circular transaction patterns..."
        
        // Simple cycle detection (depth-limited)
        let findCycles maxDepth =
            let adjacency = 
                transactions 
                |> List.groupBy (_.From) 
                |> List.map (fun (from, txs) -> from, txs |> List.map (_.To))
                |> Map.ofList
            
            let rec dfs path visited current depth =
                if depth > maxDepth then []
                elif current = List.head path && depth > 2 then [path]
                elif Set.contains current visited then []
                else
                    match Map.tryFind current adjacency with
                    | None -> []
                    | Some neighbors ->
                        neighbors
                        |> List.collect (fun next ->
                            dfs path (Set.add current visited) next (depth + 1))
            
            accounts
            |> List.map (_.Id)
            |> List.collect (fun start -> dfs [start] Set.empty start 0)
            |> List.filter (fun cycle -> cycle.Length >= 3)
            |> List.distinctBy (fun cycle -> cycle |> List.sort |> String.concat ",")
        
        findCycles 5
        |> List.map (fun cycle ->
            (cycle, 0.9, sprintf "Circular: Funds cycling through %d accounts" cycle.Length))
    
    // Combine all detected patterns
    let layeringPatterns = detectLayering ()
    let mulePatterns = detectMoneyMules ()
    let circularPatterns = detectCircular ()
    
    layeringPatterns @ mulePatterns @ circularPatterns

/// Calculate risk scores for individual accounts
let calculateRiskScores (accounts: Account list) (features: GraphFeatures list) (fraudPatterns: (string list * float * string) list) =
    
    let featureMap = features |> List.map (fun f -> f.AccountId, f) |> Map.ofList
    
    // Get accounts involved in fraud patterns
    let fraudInvolvement =
        fraudPatterns
        |> List.collect (fun (members, confidence, pattern) ->
            members |> List.map (fun m -> m, (confidence, pattern)))
        |> List.groupBy fst
        |> List.map (fun (acc, involvements) ->
            let maxConfidence = involvements |> List.map (snd >> fst) |> List.max
            let patterns = involvements |> List.map (snd >> snd) |> List.distinct
            (acc, (maxConfidence, patterns)))
        |> Map.ofList
    
    accounts
    |> List.map (fun acc ->
        let feature = featureMap |> Map.tryFind acc.Id
        let involvement = fraudInvolvement |> Map.tryFind acc.Id
        
        let reasons = ResizeArray<string>()
        let mutable score = 0.0
        
        // Factor 1: Graph structure anomalies
        match feature with
        | Some f ->
            if f.TransactionVelocity > 5.0 then
                score <- score + 0.2
                reasons.Add(sprintf "High velocity: %.1f tx/day" f.TransactionVelocity)
            
            if f.ClusteringCoefficient > 0.8 then
                score <- score + 0.15
                reasons.Add("Highly clustered connections")
            
            if f.InDegree > 5 && f.OutDegree <= 1 then
                score <- score + 0.25
                reasons.Add("Money mule topology (many in, few out)")
        | None -> ()
        
        // Factor 2: Account metadata
        if acc.Country = "XX" then
            score <- score + 0.1
            reasons.Add("Unknown jurisdiction")
        
        let accountAge = (DateTime.Now - acc.CreationDate).TotalDays
        if accountAge < 90.0 then
            score <- score + 0.15
            reasons.Add(sprintf "New account (%.0f days old)" accountAge)
        
        // Factor 3: Fraud pattern involvement
        match involvement with
        | Some (confidence, patterns) ->
            score <- score + confidence * 0.5
            patterns |> List.iter (fun p -> reasons.Add(p))
        | None -> ()
        
        // Factor 4: Pre-existing risk score
        match acc.RiskScore with
        | Some existingRisk -> 
            score <- score + existingRisk * 0.2
        | None ->
            score <- score + 0.05  // Unknown risk is slightly suspicious
            reasons.Add("No prior risk assessment")
        
        (acc.Id, min 1.0 score, reasons |> Seq.toList))
    |> List.filter (fun (_, score, _) -> score > 0.3)  // Only high-risk accounts
    |> List.sortByDescending (fun (_, score, _) -> score)

// ============================================================================
// MAIN ANALYSIS FUNCTION
// ============================================================================

/// Run full graph-based fraud analysis
let analyzeTransactionNetwork (accounts: Account list) (transactions: Transaction list) : Result<FraudAnalysisResult, exn> =
    try
        printfn "\nExtracting graph features..."
        let features = extractGraphFeatures accounts transactions
        
        printfn "Detecting communities..."
        let communitiesResult = detectCommunities accounts transactions
        
        printfn "Detecting fraud patterns..."
        let fraudPatterns = detectFraudPatterns accounts transactions features
        
        printfn "Calculating risk scores..."
        let riskScores = calculateRiskScores accounts features fraudPatterns
        
        // Calculate network metrics
        let totalVolume = transactions |> List.sumBy (_.Amount)
        let avgClustering = features |> List.averageBy (_.ClusteringCoefficient)
        let possibleEdges = accounts.Length * (accounts.Length - 1)
        let density = float transactions.Length / float possibleEdges
        
        let communities = 
            match communitiesResult with 
            | Ok c -> c 
            | Error _ -> []
        
        // Generate recommendations
        let recommendations = [
            if riskScores.Length > 0 then
                sprintf "Review %d high-risk accounts flagged by graph analysis" riskScores.Length
            
            if fraudPatterns |> List.exists (fun (_, _, p) -> p.Contains("Layering")) then
                "Investigate rapid sequential transfers - potential money laundering"
            
            if fraudPatterns |> List.exists (fun (_, _, p) -> p.Contains("Money Mule")) then
                "Review star-topology accounts - potential money mule activity"
            
            if fraudPatterns |> List.exists (fun (_, _, p) -> p.Contains("Circular")) then
                "Trace circular transaction flows - possible fraud ring"
            
            "Consider enhanced due diligence for accounts in suspicious communities"
            "Monitor for new accounts joining flagged clusters"
        ]
        
        Ok {
            HighRiskAccounts = riskScores
            SuspiciousCommunities = communities |> List.filter (fun c -> c.SuspicionScore > 0.5)
            FraudRings = fraudPatterns
            NetworkMetrics = {
                TotalAccounts = accounts.Length
                TotalTransactions = transactions.Length
                TotalVolume = totalVolume
                AverageClusteringCoefficient = avgClustering
                NetworkDensity = density
                NumberOfCommunities = communities.Length
            }
            Recommendations = recommendations
        }
    with ex ->
        Error ex

// ============================================================================
// EXAMPLE EXECUTION
// ============================================================================

printfn "=============================================="
printfn "Graph-Based Fraud Detection using Quantum QAOA"
printfn "=============================================="
printfn ""

// Generate sample network
let (accounts, transactions) = generateSampleNetwork()

printfn "Transaction Network Summary:"
printfn "  Accounts: %d" accounts.Length
printfn "  Transactions: %d" transactions.Length
printfn "  Total Volume: $%.2f" (transactions |> List.sumBy (_.Amount))
printfn ""

// Run analysis
match analyzeTransactionNetwork accounts transactions with
| Ok result ->
    printfn ""
    printfn "=============================================="
    printfn "ANALYSIS RESULTS"
    printfn "=============================================="
    printfn ""
    
    // Network metrics
    printfn "Network Metrics:"
    printfn "  Total Accounts: %d" result.NetworkMetrics.TotalAccounts
    printfn "  Total Transactions: %d" result.NetworkMetrics.TotalTransactions
    printfn "  Total Volume: $%.2f" result.NetworkMetrics.TotalVolume
    printfn "  Avg Clustering Coefficient: %.3f" result.NetworkMetrics.AverageClusteringCoefficient
    printfn "  Network Density: %.3f" result.NetworkMetrics.NetworkDensity
    printfn "  Communities Detected: %d" result.NetworkMetrics.NumberOfCommunities
    printfn ""
    
    // Fraud patterns
    printfn "Detected Fraud Patterns:"
    if result.FraudRings.IsEmpty then
        printfn "  No fraud patterns detected"
    else
        for (members, confidence, pattern) in result.FraudRings do
            printfn "  [%.0f%% confidence] %s" (confidence * 100.0) pattern
            printfn "    Involved: %s" (String.Join(", ", members))
    printfn ""
    
    // High-risk accounts
    printfn "High-Risk Accounts:"
    if result.HighRiskAccounts.IsEmpty then
        printfn "  No high-risk accounts identified"
    else
        for (accountId, score, reasons) in result.HighRiskAccounts |> List.take (min 5 result.HighRiskAccounts.Length) do
            printfn "  %s - Risk Score: %.1f%%" accountId (score * 100.0)
            for reason in reasons |> List.take (min 3 reasons.Length) do
                printfn "    - %s" reason
    printfn ""
    
    // Recommendations
    printfn "Recommendations:"
    for recommendation in result.Recommendations do
        printfn "  * %s" recommendation
    printfn ""
    
    // Summary
    let fraudRingCount = result.FraudRings.Length
    let highRiskCount = result.HighRiskAccounts.Length
    
    printfn "=============================================="
    printfn "SUMMARY"
    printfn "=============================================="
    printfn "  Fraud Patterns Found: %d" fraudRingCount
    printfn "  High-Risk Accounts: %d" highRiskCount
    printfn "  Risk Level: %s" 
        (if fraudRingCount > 2 || highRiskCount > 5 then "HIGH - Immediate investigation required"
         elif fraudRingCount > 0 || highRiskCount > 2 then "MEDIUM - Enhanced monitoring recommended"
         else "LOW - Normal activity")

| Error ex ->
    printfn "Analysis failed: %s" ex.Message

printfn ""
printfn "=============================================="
printfn "Graph Fraud Detection Example Complete!"
printfn "=============================================="
