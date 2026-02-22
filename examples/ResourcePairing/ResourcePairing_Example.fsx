// ============================================================================
// ResourcePairing Example
// ============================================================================
// Demonstrates the ResourcePairing builder for optimal matching problems
// using quantum optimization. Given a set of participants and pairwise
// compatibility scores, finds the best set of pairings that maximizes
// total compatibility.
//
// Business Use Cases:
// - Mentor-Mentee Matching: Pair based on skills and goals
// - Project Team Formation: Pair complementary skill sets
// - Kidney Exchange: Pair donors and recipients optimally
// - Interview Scheduling: Pair candidates with interviewers
//
// Usage:
//   dotnet fsi ResourcePairing_Example.fsx
//   dotnet fsi ResourcePairing_Example.fsx -- --example mentors
//   dotnet fsi ResourcePairing_Example.fsx -- --shots 2000 --quiet --output results.json
// ============================================================================

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "nuget: MathNet.Numerics, 5.0.0"
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
Cli.exitIfHelp "ResourcePairing_Example.fsx" "Quantum optimal pairing / matching" [
    { Name = "example"; Description = "Which example: all, mentors, teams, interviews"; Default = Some "all" }
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

// Helper to display pairing results
let displayResult (label: string) (result: Result<ResourcePairing.PairingResult, QuantumError>) =
    match result with
    | Ok r ->
        pr "%s Complete" label
        pr ""
        pr "  %-18s  %-18s  %10s" "Participant 1" "Participant 2" "Score"
        pr "  %-18s  %-18s  %10s" "------------------" "------------------" "----------"
        for p in r.Pairings do
            pr "  %-18s  %-18s  %10.2f" p.Participant1 p.Participant2 p.Weight
            jsonResults <- (box {| Example = label; Participant1 = p.Participant1; Participant2 = p.Participant2; Weight = p.Weight |}) :: jsonResults
            csvRows <- [ label; p.Participant1; p.Participant2; sprintf "%.2f" p.Weight; "true" ] :: csvRows
        pr ""
        pr "  Total score:  %.2f" r.TotalScore
        pr "  Paired:       %d / %d participants%s" r.ParticipantsPaired r.TotalParticipants
            (if r.IsValid then " (VALID)" else " (INVALID)")
        pr "  %s" r.Message
    | Error e ->
        pr "%s FAILED: %A" label e

// ============================================================================
// Example 1: Mentor-mentee matching
// ============================================================================

if runAll || exampleName = "mentors" then
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"
    pr " Example 1: Mentor-Mentee Matching"
    pr " Pair 6 employees into mentor-mentee pairs maximizing skill alignment."
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"

    let result =
        ResourcePairing.resourcePairing {
            participants [ "Alice"; "Bob"; "Carol"; "Dave"; "Eve"; "Frank" ]

            compatibility "Alice" "Dave"  0.9    // Senior dev <-> junior dev
            compatibility "Alice" "Eve"   0.5
            compatibility "Bob"   "Carol" 0.8    // PM <-> aspiring PM
            compatibility "Bob"   "Frank" 0.6
            compatibility "Carol" "Eve"   0.4
            compatibility "Dave"  "Frank" 0.7    // Backend <-> backend

            backend quantumBackend
            shots cliShots
        }

    displayResult "Mentors" result

// ============================================================================
// Example 2: Project team formation
// ============================================================================

if runAll || exampleName = "teams" then
    pr ""
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"
    pr " Example 2: Project Team Formation"
    pr " Pair 4 developers into 2 complementary pairs for sprint work."
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"

    let result =
        ResourcePairing.resourcePairing {
            participant "Frontend-Alex"
            participant "Backend-Jordan"
            participant "FullStack-Sam"
            participant "DevOps-Morgan"

            compatibility "Frontend-Alex"   "Backend-Jordan"  0.95   // Front + back
            compatibility "Frontend-Alex"   "FullStack-Sam"   0.60
            compatibility "Frontend-Alex"   "DevOps-Morgan"   0.40
            compatibility "Backend-Jordan"  "FullStack-Sam"   0.50
            compatibility "Backend-Jordan"  "DevOps-Morgan"   0.85   // Back + ops
            compatibility "FullStack-Sam"   "DevOps-Morgan"   0.70

            backend quantumBackend
            shots cliShots
        }

    displayResult "Teams" result

// ============================================================================
// Example 3: Interview scheduling
// ============================================================================

if runAll || exampleName = "interviews" then
    pr ""
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"
    pr " Example 3: Interview Scheduling"
    pr " Pair 8 candidates with interviewers to maximize expertise match."
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"

    let result =
        ResourcePairing.resourcePairing {
            participants [
                "Cand-ML"; "Cand-Web"; "Cand-Data"; "Cand-Sec"
                "Int-AI"; "Int-FE"; "Int-DE"; "Int-Sec"
            ]

            // Strong matches: domain alignment
            compatibility "Cand-ML"    "Int-AI"  0.95
            compatibility "Cand-Web"   "Int-FE"  0.90
            compatibility "Cand-Data"  "Int-DE"  0.92
            compatibility "Cand-Sec"   "Int-Sec" 0.88

            // Cross-domain (weaker)
            compatibility "Cand-ML"    "Int-DE"  0.60
            compatibility "Cand-Web"   "Int-AI"  0.35
            compatibility "Cand-Data"  "Int-FE"  0.40
            compatibility "Cand-Sec"   "Int-AI"  0.30

            backend quantumBackend
            shots cliShots
        }

    displayResult "Interviews" result

// --- JSON output ---
outputPath |> Option.iter (fun path ->
    Reporting.writeJson path (jsonResults |> List.rev)
    pr "JSON written to %s" path
)

// --- CSV output ---
csvPath |> Option.iter (fun path ->
    let header = [ "Example"; "Participant1"; "Participant2"; "Weight"; "Paired" ]
    Reporting.writeCsv path header (csvRows |> List.rev)
    pr "CSV written to %s" path
)

// --- Usage hints ---
if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr "-------------------------------------------"
    pr "Tip: Use --example mentors|teams|interviews to run one."
    pr "     Use --shots N to change measurement count (default 1000)."
    pr "     Use --output results.json or --csv results.csv to export."
    pr "     Use --help for all options."
