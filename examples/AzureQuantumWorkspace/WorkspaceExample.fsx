// Azure Quantum Workspace Example
// Demonstrates SDK-powered workspace management

#r "nuget: FSharp.Azure.Quantum, 1.2.4"

open System
open FSharp.Azure.Quantum.Backends.AzureQuantumWorkspace

printfn "========================================="
printfn "Azure Quantum Workspace Example"
printfn "=========================================\n"

// ============================================================================
// Example 1: Create Workspace Connection
// ============================================================================

printfn "Example 1: Create Workspace\n"

// Create workspace with your Azure Quantum credentials
// Note: Workspace implements IDisposable for proper resource cleanup
use workspace = 
    createDefault
        "your-subscription-id"          // Azure subscription ID
        "your-resource-group"            // Resource group name
        "your-workspace-name"            // Workspace name
        "eastus"                         // Azure region

printfn "✅ Workspace created: %s" workspace.Config.WorkspaceName
printfn "   Location: %s" workspace.Config.Location
printfn "   (Using 'use' keyword for automatic disposal)"
printfn ""

// ============================================================================
// Example 2: Check Quota (Requires Real Azure Quantum Workspace)
// ============================================================================

printfn "Example 2: Check Quota (requires valid credentials)\n"

// Uncomment to test with real workspace:
(*
async {
    try
        let! quota = workspace.GetTotalQuotaAsync()
        
        printfn "Quota Status:"
        match quota.Limit with
        | Some limit -> printfn "  Total Limit: %.2f credits" limit
        | None -> printfn "  Total Limit: Unlimited"
        
        match quota.Used with
        | Some used -> printfn "  Used: %.2f credits" used
        | None -> printfn "  Used: 0.00 credits"
        
        match quota.Remaining with
        | Some remaining -> 
            printfn "  Remaining: %.2f credits" remaining
            if remaining < 100.0 then
                printfn "  ⚠️  WARNING: Low quota remaining!"
        | None -> printfn "  Remaining: Unlimited"
        
    with ex ->
        printfn "❌ Could not fetch quota: %s" ex.Message
        printfn "   (This is expected without valid Azure Quantum credentials)"
} |> Async.RunSynchronously
*)

printfn "💡 To test quota checking:"
printfn "   1. Set up Azure Quantum workspace at https://portal.azure.com"
printfn "   2. Update credentials in this script"
printfn "   3. Uncomment the async block above"
printfn ""

// ============================================================================
// Example 3: List Providers (Requires Real Workspace)
// ============================================================================

printfn "Example 3: List Quantum Providers\n"

// Uncomment to test with real workspace:
(*
async {
    try
        let! providers = workspace.ListProvidersAsync()
        
        printfn "Available Quantum Providers:"
        for provider in providers do
            printfn ""
            printfn "  Provider: %s" provider.ProviderId
            match provider.CurrentAvailability with
            | Some status -> printfn "    Status: %s" status
            | None -> printfn "    Status: Unknown"
            printfn "    Targets: %d" provider.TargetCount
            
    with ex ->
        printfn "❌ Could not fetch providers: %s" ex.Message
} |> Async.RunSynchronously
*)

printfn "💡 Typical providers include:"
printfn "   - ionq (Trapped ion quantum computers)"
printfn "   - rigetti (Superconducting quantum processors)"
printfn "   - quantinuum (Trapped ion systems)"
printfn ""

// ============================================================================
// Example 4: Environment-Based Configuration (Production Pattern)
// ============================================================================

printfn "Example 4: Environment Configuration\n"

printfn "For production, use environment variables:"
printfn "  export AZURE_QUANTUM_SUBSCRIPTION_ID=\"...\""
printfn "  export AZURE_QUANTUM_RESOURCE_GROUP=\"...\""
printfn "  export AZURE_QUANTUM_WORKSPACE_NAME=\"...\""
printfn "  export AZURE_QUANTUM_LOCATION=\"eastus\""
printfn ""

match createFromEnvironment() with
| Ok ws ->
    printfn "✅ Workspace loaded from environment"
    printfn "   Workspace: %s" ws.Config.WorkspaceName
| Error msg ->
    printfn "⚠️  Environment variables not set"
    printfn "   %s" msg

printfn ""
printfn "========================================="
printfn "Example 5: Hybrid Workspace + HTTP Pattern (RECOMMENDED)"
printfn "=========================================\n"

printfn "Best practice: Use workspace for quota/discovery, HTTP backends for execution\n"

// Create a backend using the workspace
open FSharp.Azure.Quantum.Core.BackendAbstraction

printfn "Step 1: Check quota before execution"
printfn ""

// Uncomment to test with real workspace:
(*
async {
    try
        let! quota = workspace.GetTotalQuotaAsync()
        
        match quota.Remaining with
        | Some remaining when remaining < 10.0 ->
            printfn "⚠️  Low quota (%.2f credits) - stopping execution" remaining
            return None
        | Some remaining ->
            printfn "✅ Sufficient quota (%.2f credits remaining)" remaining
            return Some remaining
        | None ->
            printfn "✅ Unlimited quota"
            return Some System.Double.MaxValue
    with ex ->
        printfn "❌ Could not check quota: %s" ex.Message
        return None
} |> Async.RunSynchronously
*)

printfn ""
printfn "Step 2: Create HTTP backend for proven execution"
printfn ""

printfn "Code example:"
printfn ""
printfn "   open System.Net.Http"
printfn "   use httpClient = new HttpClient()"
printfn ""
printfn "   let backend = createIonQBackend"
printfn "       httpClient"
printfn "       \"https://your-workspace.quantum.azure.com\""
printfn "       \"ionq.simulator\""
printfn ""
printfn "   // Convert circuit to provider format"
printfn "   let circuit = quantumCircuit { H 0; CNOT 0 1 }"
printfn "   let wrapper = CircuitWrapper(circuit) :> ICircuit"
printfn ""
printfn "   match convertCircuitToProviderFormat wrapper \"ionq.simulator\" with"
printfn "   | Ok json -> "
printfn "       // Execute on backend"
printfn "       match backend.Execute wrapper 1000 with"
printfn "       | Ok result -> printfn \"Success!\""
printfn "       | Error msg -> printfn \"Error: %s\" msg"
printfn "   | Error msg -> "
printfn "       printfn \"Circuit conversion failed: %s\" msg"
printfn ""

printfn "Benefits of this hybrid approach:"
printfn "  ✅ Workspace quota checking and provider discovery"
printfn "  ✅ Circuit format conversion helpers (Phase 2)"
printfn "  ✅ Proven HTTP backends for job execution"
printfn "  ✅ Full job submission, polling, and result parsing"
printfn "  ✅ Production-ready NOW (no SDK API exploration needed)"
printfn ""

printfn "========================================="
printfn "Example 6: Circuit Conversion Helpers (Phase 2)"
printfn "=========================================\n"

printfn "Convert circuits to provider-specific formats:\n"

printfn "Code example:"
printfn ""
printfn "   // Your circuit"
printfn "   let circuit = quantumCircuit {"
printfn "       H 0"
printfn "       CNOT 0 1"
printfn "       RX (0, Math.PI / 4.0)"
printfn "   }"
printfn ""
printfn "   let wrapper = CircuitWrapper(circuit) :> ICircuit"
printfn ""
printfn "   // Convert to IonQ JSON format"
printfn "   match convertCircuitToProviderFormat wrapper \"ionq.simulator\" with"
printfn "   | Ok ionqJson ->"
printfn "       printfn \"IonQ JSON: %s\" ionqJson"
printfn "   | Error msg ->"
printfn "       printfn \"Error: %s\" msg"
printfn ""
printfn "   // Convert to Rigetti Quil format"
printfn "   match convertCircuitToProviderFormat wrapper \"rigetti.sim.qvm\" with"
printfn "   | Ok quilProgram ->"
printfn "       printfn \"Rigetti Quil: %s\" quilProgram"
printfn "   | Error msg ->"
printfn "       printfn \"Error: %s\" msg"
printfn ""

printfn "Features:"
printfn "  ✅ Automatic provider detection from target ID"
printfn "  ✅ Gate transpilation for backend compatibility"
printfn "  ✅ Support for CircuitWrapper and QaoaCircuitWrapper"
printfn "  ✅ IonQ and Rigetti providers (Quantinuum coming soon)"
printfn ""

printfn "========================================="
printfn "Next Steps"
printfn "=========================================\n"
printfn "1. Set up Azure Quantum workspace:"
printfn "   https://docs.microsoft.com/azure/quantum/"
printfn ""
printfn "2. Get your credentials from Azure Portal"
printfn ""
printfn "3. Use with circuit builders:"
printfn ""
printfn "   // Build a circuit"
printfn "   let circuit = quantumCircuit {"
printfn "       H 0"
printfn "       CNOT 0 1"
printfn "   }"
printfn ""
printfn "   // Execute on backend"
printfn "   match backend.Execute circuit 1000 with"
printfn "   | Ok result -> printfn \"Success: %A\" result"
printfn "   | Error msg -> printfn \"Error: %s\" msg"
printfn ""
