// ============================================================================
// Social Network Analyzer Example
// ============================================================================
// Demonstrates using quantum Grover's algorithm to find tight-knit communities
// (cliques) in social networks.
//
// Business Use Cases:
// - Marketing: Identify influencer groups for targeted campaigns
// - Security: Detect fraud rings through connection patterns
// - HR: Analyze team collaboration and communication networks
// - Healthcare: Track disease outbreak clusters
//
// Usage:
//   dotnet fsi SocialNetworkAnalyzer_Example.fsx
//   dotnet fsi SocialNetworkAnalyzer_Example.fsx -- --example quantum
//   dotnet fsi SocialNetworkAnalyzer_Example.fsx -- --shots 2000 --quiet --output results.json
// ============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"
open FSharp.Azure.Quantum.Examples.Common

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Business

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv
Cli.exitIfHelp "SocialNetworkAnalyzer_Example.fsx" "Quantum community detection in social networks" [
    { Name = "example"; Description = "Which example: all, classical, quantum, fraud"; Default = Some "all" }
    { Name = "shots"; Description = "Number of measurement shots"; Default = Some "1000" }
    { Name = "output"; Description = "Write results to JSON file"; Default = None }
    { Name = "csv"; Description = "Write results to CSV file"; Default = None }
    { Name = "quiet"; Description = "Suppress printed output"; Default = None }
] args

let exampleName = Cli.getOr "example" "all" args
let cliShots = Cli.getIntOr "shots" 1000 args
let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt

let runAll = (exampleName = "all")

// Accumulate results for JSON/CSV export
let mutable jsonResults : obj list = []
let mutable csvRows : string list list = []

// --- Quantum Backend (Rule 1) ---
let quantumBackend = LocalBackend() :> IQuantumBackend

// ============================================================================
// Example 1: Small Network - Classical Algorithm
// ============================================================================

if runAll || exampleName = "classical" then
    pr "=== Example 1: Small Network (Classical) ==="
    pr ""

    let classicalResult = SocialNetworkAnalyzer.socialNetwork {
        person "Alice"
        person "Bob"
        person "Carol"
        person "Dave"

        connection "Alice" "Bob"
        connection "Bob" "Carol"
        connection "Carol" "Alice"
        connection "Dave" "Alice"

        findCommunities 3

        // Explicit backend for Rule 1 compliance (classical fallback internally)
        backend quantumBackend
    }

    match classicalResult with
    | Ok result ->
        pr "Classical Analysis Complete"
        pr "  Total People: %d" result.TotalPeople
        pr "  Total Connections: %d" result.TotalConnections
        pr "  Communities Found: %d" result.Communities.Length
        pr "  Message: %s" result.Message
        pr ""

        result.Communities |> List.iter (fun comm ->
            pr "  Community: %A" comm.Members
            pr "    Strength: %.2f (%.0f%% connected)" comm.Strength (comm.Strength * 100.0)
            pr "    Internal Connections: %d" comm.InternalConnections
            pr ""
        )

        jsonResults <- (box {| example = "classical"; people = result.TotalPeople
                               connections = result.TotalConnections
                               communitiesFound = result.Communities.Length |}) :: jsonResults
        result.Communities |> List.iter (fun c ->
            csvRows <- [
                "classical"; sprintf "%A" c.Members; sprintf "%.2f" c.Strength;
                sprintf "%d" c.InternalConnections
            ] :: csvRows
        )

    | Error err ->
        pr "Error: %A" err
        pr ""

// ============================================================================
// Example 2: Larger Network - Quantum Algorithm
// ============================================================================

if runAll || exampleName = "quantum" then
    pr "=== Example 2: Larger Network (Quantum, %d shots) ===" cliShots
    pr ""

    let quantumResult = SocialNetworkAnalyzer.socialNetwork {
        people ["Alice"; "Bob"; "Carol"; "Dave"; "Eve"; "Frank"]

        connections [
            ("Alice", "Bob")
            ("Bob", "Carol")
            ("Carol", "Alice")
        ]

        connections [
            ("Dave", "Eve")
            ("Eve", "Frank")
            ("Frank", "Dave")
        ]

        connection "Carol" "Dave"

        findCommunities 3

        backend quantumBackend
        shots cliShots
    }

    match quantumResult with
    | Ok result ->
        pr "Quantum Analysis Complete"
        pr "  Total People: %d" result.TotalPeople
        pr "  Total Connections: %d" result.TotalConnections
        pr "  Communities Found: %d" result.Communities.Length
        pr "  Message: %s" result.Message
        pr ""

        if result.Communities.Length > 0 then
            result.Communities |> List.iteri (fun i comm ->
                pr "  Community %d: %A" (i + 1) comm.Members
                pr "    Strength: %.2f (%.0f%% connected)" comm.Strength (comm.Strength * 100.0)
                pr "    Internal Connections: %d" comm.InternalConnections
                pr ""
            )
        else
            pr "  No communities of size 3 found. Try smaller minimum or add more connections."
            pr ""

        jsonResults <- (box {| example = "quantum"; people = result.TotalPeople
                               connections = result.TotalConnections
                               communitiesFound = result.Communities.Length |}) :: jsonResults
        result.Communities |> List.iter (fun c ->
            csvRows <- [
                "quantum"; sprintf "%A" c.Members; sprintf "%.2f" c.Strength;
                sprintf "%d" c.InternalConnections
            ] :: csvRows
        )

    | Error err ->
        pr "Error: %A" err
        pr ""

// ============================================================================
// Example 3: Business Scenario - Fraud Detection
// ============================================================================

if runAll || exampleName = "fraud" then
    pr "=== Example 3: Fraud Detection Scenario ==="
    pr ""

    let fraudResult = SocialNetworkAnalyzer.socialNetwork {
        people [
            "Account_1001"; "Account_1002"; "Account_1003";
            "Account_1004"; "Account_1005"
        ]

        connections [
            ("Account_1001", "Account_1002")
            ("Account_1002", "Account_1003")
            ("Account_1003", "Account_1001")
            ("Account_1004", "Account_1005")
        ]

        findCommunities 3

        backend quantumBackend
        shots (cliShots * 2)
    }

    match fraudResult with
    | Ok result ->
        pr "Fraud Detection Complete"
        pr "  Accounts Analyzed: %d" result.TotalPeople
        pr "  Transactions: %d" result.TotalConnections
        pr ""

        if result.Communities.Length > 0 then
            pr "  ALERT: Potential fraud rings detected!"
            result.Communities |> List.iteri (fun i comm ->
                pr ""
                pr "  Fraud Ring %d:" (i + 1)
                pr "    Accounts: %A" comm.Members
                pr "    Ring Strength: %.2f" comm.Strength
                pr "    Circular Transactions: %d" comm.InternalConnections
            )
        else
            pr "  No suspicious circular patterns detected"
        pr ""

        jsonResults <- (box {| example = "fraud"; accounts = result.TotalPeople
                               transactions = result.TotalConnections
                               fraudRings = result.Communities.Length |}) :: jsonResults
        result.Communities |> List.iter (fun c ->
            csvRows <- [
                "fraud"; sprintf "%A" c.Members; sprintf "%.2f" c.Strength;
                sprintf "%d" c.InternalConnections
            ] :: csvRows
        )

    | Error err ->
        pr "Error: %A" err
        pr ""

// --- JSON output ---
outputPath |> Option.iter (fun path ->
    Reporting.writeJson path (jsonResults |> List.rev)
    pr "JSON written to %s" path
)

// --- CSV output ---
csvPath |> Option.iter (fun path ->
    let header = ["Example"; "Members"; "Strength"; "InternalConnections"]
    Reporting.writeCsv path header (csvRows |> List.rev)
    pr "CSV written to %s" path
)

// --- Usage hints ---
if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr "-------------------------------------------"
    pr "Tip: Use --example classical|quantum|fraud to run a single example."
    pr "     Use --shots N to change measurement count (default 1000)."
    pr "     Use --output results.json or --csv results.csv to export."
    pr "     Use --help for all options."
