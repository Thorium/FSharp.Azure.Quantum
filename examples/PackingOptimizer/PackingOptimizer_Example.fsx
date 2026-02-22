// ============================================================================
// PackingOptimizer Example
// ============================================================================
// Demonstrates the PackingOptimizer builder for bin-packing problems using
// quantum optimization. Given a set of items with sizes and a bin capacity,
// finds the assignment of items to bins that minimizes the number of bins used.
//
// Business Use Cases:
// - Container Loading: Pack shipments into fewest containers
// - Server Allocation: Assign workloads to fewest VMs
// - Storage Optimization: Fit files into fewest storage volumes
// - Truck Loading: Fit packages into fewest delivery trucks
//
// Usage:
//   dotnet fsi PackingOptimizer_Example.fsx
//   dotnet fsi PackingOptimizer_Example.fsx -- --example shipping
//   dotnet fsi PackingOptimizer_Example.fsx -- --shots 2000 --quiet --output results.json
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
Cli.exitIfHelp "PackingOptimizer_Example.fsx" "Quantum bin-packing optimization" [
    { Name = "example"; Description = "Which example: all, shipping, servers, storage"; Default = Some "all" }
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

// Helper to display packing results
let displayResult (label: string) (result: Result<PackingOptimizer.PackingResult, QuantumError>) =
    match result with
    | Ok r ->
        pr "%s Complete" label
        pr ""
        pr "  %-20s  %10s  %6s" "Item" "Size" "Bin"
        pr "  %-20s  %10s  %6s" "--------------------" "----------" "------"
        let sorted = r.Assignments |> List.sortBy (fun a -> a.BinIndex, a.Item.Id)
        for a in sorted do
            pr "  %-20s  %10.1f  %6d" a.Item.Id a.Item.Size a.BinIndex
            jsonResults <- (box {| Example = label; Item = a.Item.Id; Size = a.Item.Size; Bin = a.BinIndex |}) :: jsonResults
            csvRows <- [ label; a.Item.Id; sprintf "%.1f" a.Item.Size; string a.BinIndex; "true" ] :: csvRows
        pr ""
        pr "  Bins used:    %d" r.BinsUsed
        pr "  Items:        %d / %d assigned%s" r.ItemsAssigned r.TotalItems
            (if r.IsValid then " (VALID)" else " (INVALID)")
        pr "  %s" r.Message
    | Error e ->
        pr "%s FAILED: %A" label e

// ============================================================================
// Example 1: Container shipping
// ============================================================================

if runAll || exampleName = "shipping" then
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"
    pr " Example 1: Container Shipping"
    pr " Pack 6 shipments into containers with 100-unit capacity."
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"

    let result =
        PackingOptimizer.packingOptimizer {
            containerCapacity 100.0

            item "Crate-A"    35.0
            item "Crate-B"    45.0
            item "Crate-C"    20.0
            item "Crate-D"    55.0
            item "Crate-E"    30.0
            item "Crate-F"    40.0

            backend quantumBackend
            shots cliShots
        }

    displayResult "Shipping" result

// ============================================================================
// Example 2: Server allocation
// ============================================================================

if runAll || exampleName = "servers" then
    pr ""
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"
    pr " Example 2: Server Allocation"
    pr " Assign 5 workloads to VMs with 8 GB memory each."
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"

    let result =
        PackingOptimizer.packingOptimizer {
            containerCapacity 8.0

            item "WebAPI"      2.5
            item "Database"    4.0
            item "Cache"       1.5
            item "Worker"      3.0
            item "Monitoring"  1.0

            backend quantumBackend
            shots cliShots
        }

    displayResult "Servers" result

// ============================================================================
// Example 3: Storage volumes
// ============================================================================

if runAll || exampleName = "storage" then
    pr ""
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"
    pr " Example 3: Storage Volume Optimization"
    pr " Fit 7 datasets into 500 GB volumes."
    pr "â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”"

    let result =
        PackingOptimizer.packingOptimizer {
            containerCapacity 500.0

            item "UserData"     180.0
            item "Logs"         120.0
            item "Analytics"    200.0
            item "Backups"      150.0
            item "MediaAssets"  280.0
            item "Configs"       30.0
            item "Temp"          90.0

            backend quantumBackend
            shots cliShots
        }

    displayResult "Storage" result

// --- JSON output ---
outputPath |> Option.iter (fun path ->
    Reporting.writeJson path (jsonResults |> List.rev)
    pr "JSON written to %s" path
)

// --- CSV output ---
csvPath |> Option.iter (fun path ->
    let header = [ "Example"; "Item"; "Size"; "Bin"; "Assigned" ]
    Reporting.writeCsv path header (csvRows |> List.rev)
    pr "CSV written to %s" path
)

// --- Usage hints ---
if not quiet && outputPath.IsNone && csvPath.IsNone then
    pr "-------------------------------------------"
    pr "Tip: Use --example shipping|servers|storage to run one."
    pr "     Use --shots N to change measurement count (default 1000)."
    pr "     Use --output results.json or --csv results.csv to export."
    pr "     Use --help for all options."
