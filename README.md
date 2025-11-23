# FSharp.Azure.Quantum

F# library for Azure Quantum - providing functional programming abstractions over Azure Quantum REST API.

## Project Status

✅ **Layer 1: HTTP Client Core** (TKT-15) - Complete  
✅ **Classical TSP Solver** (TKT-16) - Complete  
🚧 **In Development** - Layer 2 and beyond

## Features

### Azure Quantum HTTP Client
- Full Azure Quantum REST API client with async/await
- Azure.Identity authentication (DefaultAzureCredential, ManagedIdentity)
- Retry logic with exponential backoff + jitter
- Cost tracking and budget limits
- Structured logging with Microsoft.Extensions.Logging
- Comprehensive error handling (transient, rate limit, quota, auth)

### Classical TSP Solver
- Nearest Neighbor initialization (O(n²))
- 2-opt local search optimization
- Configurable iteration limits
- Support for Euclidean and custom distance matrices
- Performance: solves 50-city TSP in <1 second
- Tour quality: within 10% of optimal for test instances

## Architecture

This library provides a layered architecture for quantum computing with Azure Quantum:

- **Layer 1: HTTP Client Core** ✅ - Low-level Azure Quantum REST API client
- **Classical Solvers** ✅ - TSP solver with 2-opt algorithm
- **Layer 2: Circuit Builder** 🚧 - Quantum circuit construction
- **Layer 3: QAOA API** 🚧 - Quantum Approximate Optimization Algorithm
- **Layer 4: Domain Builders** 🚧 - TSP and Portfolio optimization
- **Quantum Advisor** 🚧 - Classical vs Quantum execution decision

## Getting Started

### Azure Quantum Client

```fsharp
open FSharp.Azure.Quantum.Core.Client
open FSharp.Azure.Quantum.Core.Types

// Create HTTP client
let httpClient = new System.Net.Http.HttpClient()

// Configure client
let config = createConfig 
    "your-subscription-id"
    "your-resource-group"
    "your-workspace-name"
    httpClient

// Create client instance
let client = QuantumClient(config)

// Submit a quantum job
let submission = {
    JobId = System.Guid.NewGuid().ToString()
    Target = "ionq.simulator"
    Name = Some "My Quantum Job"
    InputData = circuitData
    InputDataFormat = QIR_V1
    InputParams = Map.ofList [("shots", 1000 :> obj)]
    Tags = Map.empty
}

let! result = client.SubmitJobAsync(submission)
```

### Classical TSP Solver

```fsharp
open FSharp.Azure.Quantum.Classical.TspSolver

// Define cities (x, y coordinates)
let cities = [|
    (0.0, 0.0)
    (1.0, 0.0)
    (1.0, 1.0)
    (0.0, 1.0)
|]

// Solve TSP
let solution = solve cities defaultConfig

printfn "Tour: %A" solution.Tour
printfn "Length: %.2f" solution.TourLength
printfn "Time: %.2fms" solution.ElapsedMs
```

## Development

### Prerequisites

- .NET 8.0 SDK or later
- F# compiler

### Build

```bash
dotnet build
```

### Test

```bash
dotnet test
```

## Dependencies

- Azure.Identity - Azure AD authentication
- Microsoft.Extensions.Logging - Structured logging
- System.Text.Json - JSON serialization

## License

TBD
