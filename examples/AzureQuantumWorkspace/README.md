# Azure Quantum Workspace Example

## Overview

This example demonstrates **production-ready Azure Quantum workspace management** with quota checking, circuit conversion, and SDK-powered job execution.

## What This Example Covers

### ✅ Workspace Management
- Create workspace connections (default config or environment variables)
- Check quota usage and limits
- Discover available quantum providers (IonQ, Rigetti, Quantinuum)
- Manage credentials and authentication

### ✅ Circuit Execution
- Convert circuits to provider-specific formats (IonQ JSON, Rigetti Quil)
- Submit jobs to quantum hardware/simulators
- Poll for job completion with smart exponential backoff
- Parse and analyze results

### ✅ Quota & Cost Control
- Real-time quota checking before execution
- Cost guards to prevent overspending
- Usage tracking and reporting
- Multi-provider quota aggregation

## Examples Included

The `WorkspaceExample.fsx` script contains **9 comprehensive examples:**

### Example 1: Basic Workspace Creation
```fsharp
open FSharp.Azure.Quantum.Backends.AzureQuantumWorkspace

// Create workspace with explicit configuration
use workspace = 
    createDefault 
        "your-subscription-id"
        "your-resource-group"
        "your-workspace-name"
        "eastus"

printfn "✅ Connected to workspace: %s" workspace.Config.WorkspaceName
```

### Example 2: Environment-Based Configuration
```bash
# Set environment variables
export AZURE_QUANTUM_SUBSCRIPTION_ID="..."
export AZURE_QUANTUM_RESOURCE_GROUP="..."
export AZURE_QUANTUM_WORKSPACE_NAME="..."
export AZURE_QUANTUM_LOCATION="eastus"
```

```fsharp
// Load from environment
match createFromEnvironment() with
| Ok workspace -> 
    printfn "✅ Workspace loaded from environment"
| Error msg -> 
    printfn "⚠️ Config error: %s" msg
```

### Example 3: Quota Checking
```fsharp
async {
    let! quota = workspace.GetTotalQuotaAsync()
    
    match quota.Remaining with
    | Some remaining -> 
        printfn "💰 Quota remaining: %.2f credits" remaining
        if remaining < 10.0 then
            printfn "⚠️  Low quota - consider recharging"
    | None -> 
        printfn "✅ Unlimited quota"
}
```

### Example 4: Provider Discovery
```fsharp
async {
    let! providers = workspace.GetProvidersAsync()
    
    printfn "Available providers:"
    providers |> List.iter (fun p ->
        printfn "  - %s (status: %s)" p.ProviderId p.ProvisioningState)
}
```

### Example 5: Circuit Conversion (IonQ)
```fsharp
// Build quantum circuit
let circuit = quantumCircuit { 
    H 0
    CNOT 0 1
    RX (0, Math.PI / 4.0)
}

let wrapper = CircuitWrapper(circuit) :> ICircuit

// Convert to IonQ JSON format
match convertCircuitToProviderFormat wrapper "ionq.simulator" with
| Ok ionqJson -> 
    printfn "IonQ Circuit JSON:\n%s" ionqJson
| Error msg -> 
    printfn "Conversion error: %s" msg
```

### Example 6: Circuit Conversion (Rigetti Quil)
```fsharp
// Convert same circuit to Rigetti Quil
match convertCircuitToProviderFormat wrapper "rigetti.sim.qvm" with
| Ok quilProgram -> 
    printfn "Rigetti Quil Program:\n%s" quilProgram
| Error msg -> 
    printfn "Conversion error: %s" msg
```

### Example 7: HTTP Backend Execution
```fsharp
// Create HTTP backend for proven execution
use httpClient = new HttpClient()
let backend = createIonQBackend
    httpClient
    "https://your-workspace.quantum.azure.com"
    "ionq.simulator"

// Execute circuit
match backend.Execute wrapper 1000 with
| Ok result ->
    printfn "✅ Job completed!"
    printfn "   Backend: %s" result.BackendName
    printfn "   Shots: %d" result.NumShots
    
    // Analyze results
    let counts = result.Measurements |> Array.countBy id
    counts |> Array.iter (fun (bitstring, count) ->
        printfn "   %A: %d times" bitstring count)
| Error msg ->
    printfn "❌ Execution failed: %s" msg
```

### Example 8: SDK Backend with Quota Check
```fsharp
async {
    // Check quota first
    let! quota = workspace.GetTotalQuotaAsync()
    
    match quota.Remaining with
    | Some remaining when remaining >= 10.0 ->
        // Create SDK backend (automatic job management)
        let backend = createFromWorkspace workspace "ionq.simulator"
        
        // Execute with automatic polling
        match backend.Execute circuit 1000 with
        | Ok result ->
            printfn "✅ SDK execution successful!"
            printfn "   Job ID: %s" (result.Metadata.["job_id"] :?> string)
        | Error msg ->
            printfn "❌ Error: %s" msg
    | Some remaining ->
        printfn "⚠️  Insufficient quota: %.2f credits" remaining
    | None ->
        printfn "✅ Proceeding with unlimited quota"
}
```

### Example 9: Full Production Pipeline
```fsharp
// Complete production workflow
async {
    // 1. Check quota
    let! quota = workspace.GetTotalQuotaAsync()
    
    match quota.Remaining with
    | Some r when r < 10.0 -> 
        return Error "Low quota"
    | _ ->
        // 2. Convert circuit
        match convertCircuitToProviderFormat circuit "ionq.simulator" with
        | Ok json ->
            // 3. Execute
            let backend = createFromWorkspace workspace "ionq.simulator"
            return backend.Execute circuit 1000
        | Error msg ->
            return Error msg
}
```

## How to Run

### Prerequisites

1. **Azure Quantum Workspace** - Create at [portal.azure.com](https://portal.azure.com)
2. **Credentials** - Service principal or managed identity
3. **NuGet Packages**:
   ```bash
   dotnet add package FSharp.Azure.Quantum
   dotnet add package Microsoft.Azure.Quantum.Client
   ```

### Run the Example

```bash
# Option 1: Direct execution
cd examples/AzureQuantumWorkspace
dotnet fsi WorkspaceExample.fsx

# Option 2: With environment variables
export AZURE_QUANTUM_SUBSCRIPTION_ID="your-sub-id"
export AZURE_QUANTUM_RESOURCE_GROUP="quantum-rg"
export AZURE_QUANTUM_WORKSPACE_NAME="my-workspace"
export AZURE_QUANTUM_LOCATION="eastus"
dotnet fsi WorkspaceExample.fsx
```

## Backend Comparison

| Feature | LocalBackend | HTTP Backend | SDK Backend |
|---------|-------------|--------------|-------------|
| **Setup** | None | HttpClient + URL | Workspace object |
| **Quota Checking** | ❌ | ❌ | ✅ |
| **Provider Discovery** | ❌ | ❌ | ✅ |
| **Job Polling** | ❌ (instant) | Manual | ✅ Automatic |
| **Resource Cleanup** | ❌ | Manual | ✅ IDisposable |
| **Circuit Conversion** | ❌ | Manual | ✅ Automatic |
| **Max Qubits** | 20 | 29 (IonQ) / 40 (Rigetti) | 29 (IonQ) / 40 (Rigetti) |
| **Cost** | Free | Paid | Paid |
| **Best For** | Testing | Manual control | Full integration |

## When to Use Each Backend

### LocalBackend
- ✅ Development and testing
- ✅ Small circuits (<20 qubits)
- ✅ Free tier experimentation
- ❌ Production workloads

### HTTP Backend
- ✅ Production deployments
- ✅ Custom job management
- ✅ Fine-grained control
- ✅ Proven stability

### SDK Backend (This Example)
- ✅ Full workspace features
- ✅ Automatic quota management
- ✅ Easier setup
- ✅ Complete Azure integration
- ✅ Production-ready

## Circuit Format Conversion

The library automatically converts circuits to provider-specific formats:

### IonQ JSON Format
```json
{
  "qubits": 2,
  "circuit": [
    {"gate": "h", "target": 0},
    {"gate": "cnot", "control": 0, "target": 1}
  ]
}
```

### Rigetti Quil Format
```quil
DECLARE ro BIT[2]
H 0
CNOT 0 1
MEASURE 0 ro[0]
MEASURE 1 ro[1]
```

## Cost Management

**Recommended Practices:**

1. **Check quota before expensive operations**
   ```fsharp
   let! quota = workspace.GetTotalQuotaAsync()
   ```

2. **Set cost guards in configuration**
   ```fsharp
   let config = { MaxCostUSD = Some 50.0; ... }
   ```

3. **Use simulators for development**
   ```fsharp
   let backend = createIonQBackend(..., "ionq.simulator")  // Free tier
   ```

4. **Monitor usage regularly**
   ```fsharp
   let! usage = workspace.GetUsageAsync()
   ```

## Related Documentation

- **[Backend Switching Guide](../../docs/backend-switching.md)** - Detailed backend comparison
- **[Getting Started](../../docs/getting-started.md)** - Quick start guide
- **[API Reference](../../docs/api-reference.md)** - Complete API docs
- **[Azure Quantum Docs](https://docs.microsoft.com/azure/quantum/)** - Official Azure docs

## Troubleshooting

### "Workspace not found"
- Verify subscription ID, resource group, workspace name
- Check Azure portal for correct values
- Ensure workspace is in correct region

### "Insufficient quota"
- Check quota: `workspace.GetTotalQuotaAsync()`
- Contact Azure support to increase limits
- Use free tier simulators for testing

### "Circuit conversion failed"
- Verify backend target ID (e.g., "ionq.simulator")
- Check if gates are supported by provider
- Review circuit structure (qubit count, gate types)

### "Authentication failed"
- Verify credentials (service principal, managed identity)
- Check Azure RBAC permissions (Quantum Contributor role)
- Ensure token hasn't expired

## Expected Output

```
=========================================
Azure Quantum Workspace Example
=========================================

Example 1: Create Workspace
✅ Connected to workspace: my-quantum-workspace

Example 2: Environment Configuration
✅ Workspace loaded from environment

Example 3: Quota Check
💰 Total quota: 100.00 credits
💰 Remaining: 87.34 credits
✅ Sufficient quota for execution

Example 4: Provider Discovery
Available providers:
  - ionq (status: Ready)
  - rigetti (status: Ready)
  - quantinuum (status: Ready)

Example 5-6: Circuit Conversion
✅ IonQ JSON format generated
✅ Rigetti Quil format generated

Example 7: HTTP Backend Execution
✅ Job submitted: job-12345
⏳ Polling... Status: Waiting
⏳ Polling... Status: Executing  
✅ Job completed!
   |00⟩: 487 times
   |11⟩: 513 times

Example 8-9: SDK Backend with Quota
✅ Quota verified (87.34 credits)
✅ SDK execution successful!
✅ All examples completed!
```

## Further Reading

- **[Azure Quantum Pricing](https://azure.microsoft.com/pricing/details/azure-quantum/)** - Cost calculator
- **[IonQ Documentation](https://ionq.com/docs)** - IonQ-specific details
- **[Rigetti Documentation](https://docs.rigetti.com/)** - Rigetti-specific details
- **[Quantum Circuit Optimization](https://arxiv.org/abs/2105.05706)** - Advanced techniques
