#!/usr/bin/env dotnet fsi
// ============================================================================
// Azure Quantum Workspace Management
// ============================================================================
//
// Demonstrates SDK-powered workspace management for Azure Quantum:
// creating connections, checking quota, listing providers, environment
// config, and backend comparison (Local vs HTTP vs SDK).
//
// Most examples print code patterns since Azure credentials are required
// for real execution. The workspace creation and environment config check
// run without credentials.
//
// Examples: create, env-config, backends, all.
// Extensible starting point for Azure Quantum workspace integration.
//
// ============================================================================

//#r "nuget: FSharp.Azure.Quantum"
#r "nuget: Microsoft.Azure.Quantum.Client"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum.Backends.AzureQuantumWorkspace
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// --- Quantum Backend (Rule 1) ---
// LocalBackend for local development; workspace creates cloud backends
let quantumBackend = LocalBackend() :> IQuantumBackend

// --- CLI ---
let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "WorkspaceExample.fsx"
    "Azure Quantum workspace management: create, env config, backend comparison"
    [ { Name = "example"; Description = "Which example (all|create|env-config|backends)"; Default = Some "all" }
      { Name = "subscription"; Description = "Azure subscription ID"; Default = Some "your-subscription-id" }
      { Name = "resource-group"; Description = "Azure resource group"; Default = Some "your-resource-group" }
      { Name = "workspace-name"; Description = "Azure Quantum workspace name"; Default = Some "your-workspace-name" }
      { Name = "location"; Description = "Azure region"; Default = Some "eastus" }
      { Name = "output"; Description = "Write results to JSON file"; Default = None }
      { Name = "csv"; Description = "Write results to CSV file"; Default = None }
      { Name = "quiet"; Description = "Suppress console output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args
let exampleArg = Cli.getOr "example" "all" args
let subscription = Cli.getOr "subscription" "your-subscription-id" args
let resourceGroup = Cli.getOr "resource-group" "your-resource-group" args
let workspaceName = Cli.getOr "workspace-name" "your-workspace-name" args
let location = Cli.getOr "location" "eastus" args

let pr fmt = Printf.ksprintf (fun s -> if not quiet then printfn "%s" s) fmt
let shouldRun key = exampleArg = "all" || exampleArg = key

// --- Result Tracking ---

type ExampleResult =
    { Name: string
      Label: string
      Status: string
      Detail: string }

let mutable jsonResults : ExampleResult list = []
let mutable csvRows : string list list = []

let record (r: ExampleResult) =
    jsonResults <- jsonResults @ [ r ]
    csvRows <- csvRows @ [ [ r.Name; r.Label; r.Status; r.Detail ] ]

// ============================================================================
// EXAMPLE 1: Create Workspace Connection
// ============================================================================

if shouldRun "create" then
    pr "=== Example 1: Create Workspace Connection ==="
    pr ""

    let workspace =
        createDefault subscription resourceGroup workspaceName location

    pr "  [OK] Workspace created: %s" workspace.Config.WorkspaceName
    pr "  Location: %s" workspace.Config.Location
    pr ""

    pr "  Quota checking (requires valid Azure credentials):"
    pr "    let! quota = workspace.GetTotalQuotaAsync()"
    pr "    match quota.Remaining with"
    pr "    | Some remaining -> printfn \"Credits: %%.2f\" remaining"
    pr "    | None -> printfn \"Unlimited\""
    pr ""

    pr "  Provider listing (requires valid Azure credentials):"
    pr "    let! providers = workspace.ListProvidersAsync()"
    pr "    providers |> Seq.iter (fun p -> printfn \"%%s\" p.ProviderId)"
    pr ""
    pr "  Typical providers: ionq, rigetti, quantinuum"
    pr ""

    record
        { Name = "create"; Label = "Workspace Connection"
          Status = "OK"
          Detail = sprintf "%s @ %s" workspace.Config.WorkspaceName workspace.Config.Location }

// ============================================================================
// EXAMPLE 2: Environment-Based Configuration
// ============================================================================

if shouldRun "env-config" then
    pr "=== Example 2: Environment Configuration ==="
    pr ""

    pr "  Required environment variables:"
    pr "    AZURE_QUANTUM_SUBSCRIPTION_ID"
    pr "    AZURE_QUANTUM_RESOURCE_GROUP"
    pr "    AZURE_QUANTUM_WORKSPACE_NAME"
    pr "    AZURE_QUANTUM_LOCATION"
    pr ""

    match createFromEnvironment() with
    | Ok ws ->
        pr "  [OK] Workspace loaded from environment: %s" ws.Config.WorkspaceName

        record
            { Name = "env-config"; Label = "Environment Config"
              Status = "OK"
              Detail = ws.Config.WorkspaceName }
    | Error err ->
        pr "  [WARN] Environment variables not set: %s" err.Message
        pr "  (This is expected without Azure Quantum credentials)"

        record
            { Name = "env-config"; Label = "Environment Config"
              Status = "NOT_SET"
              Detail = err.Message }
    pr ""

// ============================================================================
// EXAMPLE 3: Backend Comparison & Patterns
// ============================================================================

if shouldRun "backends" then
    pr "=== Example 3: Backend Comparison ==="
    pr ""

    // --- Local Backend (actually runs) ---
    pr "  1. Local Simulator Backend"
    pr "     let backend = LocalBackend() :> IQuantumBackend"
    pr "     + Fast (milliseconds), no Azure account needed"
    pr "     + Good for development and testing (<20 qubits)"
    pr ""

    // Demonstrate local backend is working
    pr "     Local backend active: %s (state type: %A)" quantumBackend.Name quantumBackend.NativeStateType
    pr ""

    // --- HTTP Backend ---
    pr "  2. HTTP Backend (recommended for production)"
    pr "     use httpClient = new HttpClient()"
    pr "     let backend = createIonQBackend httpClient workspaceUrl \"ionq.simulator\""
    pr "     + Production-proven, direct REST API control"
    pr "     + Lower-level error handling"
    pr ""

    // --- SDK Backend ---
    pr "  3. SDK Backend (full integration)"
    pr "     use workspace = createDefault \"sub\" \"rg\" \"ws\" \"eastus\""
    pr "     let backend = createFromWorkspace workspace \"ionq.simulator\""
    pr "     + Full Azure Quantum integration"
    pr "     + Quota checking and provider discovery"
    pr "     + Automatic job polling with backoff"
    pr ""

    // --- Hybrid Pattern ---
    pr "  Recommended pattern: workspace for quota/discovery, HTTP for execution"
    pr ""
    pr "  Code pattern:"
    pr "    // Check quota"
    pr "    let! quota = workspace.GetTotalQuotaAsync()"
    pr "    match quota.Remaining with"
    pr "    | Some r when r < 10.0 -> printfn \"Low quota\""
    pr "    | _ ->"
    pr "        // Execute via HTTP backend"
    pr "        let result = backend.Execute wrapper 1000"
    pr ""

    // --- Circuit Conversion ---
    pr "  Circuit format conversion:"
    pr "    match convertCircuitToProviderFormat wrapper \"ionq.simulator\" with"
    pr "    | Ok json -> printfn \"IonQ: %%s\" json"
    pr "    | Error e -> printfn \"Error: %%s\" e"
    pr ""

    pr "  When to use each:"
    pr "    Local  - Development, testing, small circuits"
    pr "    HTTP   - Production workloads, proven stability"
    pr "    SDK    - Full workspace features, quota management"
    pr ""

    record
        { Name = "backends"; Label = "Backend Comparison"
          Status = "OK"
          Detail = sprintf "local backend=%s type=%A" quantumBackend.Name quantumBackend.NativeStateType }

// --- JSON output ---

outputPath
|> Option.iter (fun path ->
    let payload =
        jsonResults
        |> List.map (fun r ->
            dict [
                "name", box r.Name
                "label", box r.Label
                "status", box r.Status
                "detail", box r.Detail ])
    Reporting.writeJson path payload)

// --- CSV output ---

csvPath
|> Option.iter (fun path ->
    let header = [ "name"; "label"; "status"; "detail" ]
    Reporting.writeCsv path header csvRows)

// --- Summary ---

if not quiet then
    pr ""
    pr "=== Summary ==="
    jsonResults
    |> List.iter (fun r ->
        pr "  [%s] %-25s %s" r.Status r.Label r.Detail)
    pr ""
    pr "Next steps:"
    pr "  1. Set up Azure Quantum workspace: https://docs.microsoft.com/azure/quantum/"
    pr "  2. Set environment variables or pass --subscription, --resource-group, etc."
    pr "  3. Choose backend: Local (testing), HTTP (production), SDK (full integration)"
    pr "  4. See examples/CircuitBuilder/ for circuit building"
    pr ""

if not quiet && outputPath.IsNone && csvPath.IsNone && (argv |> Array.isEmpty) then
    pr "Tip: Use --output results.json or --csv results.csv to export data."
    pr "     Use --example create to run a single example."
    pr "     Pass --subscription/--resource-group/--workspace-name for real Azure config."
    pr "     Run with --help for all options."
