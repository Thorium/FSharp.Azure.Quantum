# FSharp.Azure.Quantum

F# library for Azure Quantum - providing functional programming abstractions over Azure Quantum REST API.

## Project Status

🚧 **In Development** - TKT-15: Layer 1 HTTP Client Core

## Architecture

This library provides a layered architecture for quantum computing with Azure Quantum:

- **Layer 1: HTTP Client Core** (TKT-15) - Low-level Azure Quantum REST API client
- **Layer 2: Circuit Builder** - Quantum circuit construction
- **Layer 3: QAOA API** - Quantum Approximate Optimization Algorithm
- **Layer 4: Domain Builders** - TSP and Portfolio optimization

## Getting Started

```fsharp
// Coming soon - API under development
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
