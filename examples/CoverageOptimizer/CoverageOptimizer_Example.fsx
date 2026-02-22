// ============================================================================
// CoverageOptimizer Example
// ============================================================================
// Demonstrates the CoverageOptimizer builder for solving set-cover problems
// using quantum optimization. Given a universe of elements and a collection
// of options (each covering some subset at a cost), finds the cheapest set
// of options that covers every element.
//
// Business Use Cases:
// - Facility Placement: Cover all neighborhoods with minimum cost
// - Test Suite Selection: Cover all requirements with fewest tests
// - Sensor Deployment: Cover all zones with minimum hardware
// - Service Coverage: Cover all regions with fewest service centers
//
// Usage:
//   dotnet fsi CoverageOptimizer_Example.fsx
//   dotnet fsi CoverageOptimizer_Example.fsx -- --example sensors
//   dotnet fsi CoverageOptimizer_Example.fsx -- --shots 2000 --quiet --output results.json
// ============================================================================

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
Cli.exitIfHelp "CoverageOptimizer_Example.fsx" "Quantum set-cover optimization" [
    { Name = "example"; Description = "Which example: all, facilities, sensors, tests"; Default = Some "all" }
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

// Helper to display coverage results
let displayResult (label: string) (result: Result<CoverageOptimizer.CoverageResult, QuantumError>) =
    match result with
    | Ok r ->
        pr "%s Complete" label
        pr ""
        pr "  %-20s  %-8s  %s" "Option" "Cost" "Covers"
        pr "  %-20s  %-8s  %s" "--------------------" "--------" "-------------------"
        for opt in r.SelectedOptions do
            let covers = opt.CoveredElements |> List.map string |> String.concat ","
            pr "  %-20s  %8.1f  [%s]" opt.Id opt.Cost covers
            jsonResults <- (box {| Example = label; Option = opt.Id; CoveredElements = covers; Cost = opt.Cost; Selected = true |}) :: jsonResults
            csvRows <- [ label; opt.Id; covers; sprintf "%.1f" opt.Cost; "true" ] :: csvRows
        pr ""
        pr "  Total cost:     %.1f" r.TotalCost
        pr "  Coverage:       %d / %d elements%s" r.ElementsCovered r.TotalElements
            (if r.IsComplete then " (COMPLETE)" else " (PARTIAL)")
        pr "  %s" r.Message
    | Error e ->
        pr "%s FAILED: %A" label e

// ============================================================================
// Example 1: Facility placement
// ============================================================================

if runAll || exampleName = "facilities" then
    pr "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    pr " Example 1: Facility Placement"
    pr " Place service centers to cover 8 neighborhoods at minimum total cost."
    pr "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

    let result =
        CoverageOptimizer.coverageOptimizer {
            universeSize 8

            option "CenterNorth"  [ 0; 1; 2 ]       120.0
            option "CenterSouth"  [ 5; 6; 7 ]       110.0
            option "CenterEast"   [ 2; 3; 4 ]       100.0
            option "CenterWest"   [ 0; 4; 5 ]        95.0
            option "CenterHub"    [ 1; 3; 5; 7 ]    150.0

            backend quantumBackend
            shots cliShots
        }

    displayResult "Facilities" result

// ============================================================================
// Example 2: Sensor deployment
// ============================================================================

if runAll || exampleName = "sensors" then
    pr ""
    pr "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    pr " Example 2: Sensor Deployment"
    pr " Deploy sensors to monitor 6 zones with minimum hardware cost."
    pr "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

    let result =
        CoverageOptimizer.coverageOptimizer {
            universeSize 6

            option "SensorA" [ 0; 1 ]     30.0
            option "SensorB" [ 1; 2; 3 ]  45.0
            option "SensorC" [ 3; 4 ]     25.0
            option "SensorD" [ 4; 5 ]     20.0
            option "SensorE" [ 0; 2; 5 ]  50.0

            backend quantumBackend
            shots cliShots
        }

    displayResult "Sensors" result

// ============================================================================
// Example 3: Test suite selection
// ============================================================================

if runAll || exampleName = "tests" then
    pr ""
    pr "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    pr " Example 3: Test Suite Selection"
    pr " Select minimum test cases to cover 10 requirements."
    pr "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

    let result =
        CoverageOptimizer.coverageOptimizer {
            universeSize 10

            option "TestLogin"       [ 0; 1; 2 ]          5.0
            option "TestCheckout"    [ 2; 3; 4; 5 ]       8.0
            option "TestSearch"      [ 1; 6; 7 ]          6.0
            option "TestProfile"     [ 0; 8; 9 ]          4.0
            option "TestNavigation"  [ 5; 6; 7; 8; 9 ]   10.0
            option "TestSmoke"       [ 0; 3; 6; 9 ]       7.0

            backend quantumBackend
            shots cliShots
        }

    displayResult "TestSuite" result

// --- JSON output ---
outputPath |> Option.iter (fun path ->
    Reporting.writeJson path (jsonResults |> List.rev)
    pr "JSON written to %s" path
)

// --- CSV output ---
csvPath |> Option.iter (fun path ->
    let header = [ "Example"; "Option"; "CoveredElements"; "Cost"; "Selected" ]
    Reporting.writeCsv path header (csvRows |> List.rev)
    pr "CSV written to %s" path
)

// --- Usage hints ---
if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr "-------------------------------------------"
    pr "Tip: Use --example facilities|sensors|tests to run one."
    pr "     Use --shots N to change measurement count (default 1000)."
    pr "     Use --output results.json or --csv results.csv to export."
    pr "     Use --help for all options."
