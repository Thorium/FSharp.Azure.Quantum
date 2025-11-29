# Investment Portfolio Optimization - C# Interop Example

**Demonstrates C# interoperability with the F# FSharp.Azure.Quantum library for portfolio optimization.**

## Overview

This C# console application example demonstrates seamless interoperability between C# and the F# `FSharp.Azure.Quantum` library for quantum-ready portfolio optimization. It shows how C# developers can leverage F#'s powerful quantum optimization capabilities without writing F# code.

## Running the Example

```bash
cd examples/InvestmentPortfolio_CSharp/PortfolioExample
dotnet run
```

## What This Example Demonstrates

1. **C# ↔ F# Interoperability**: Natural C# consumption of F# quantum library
2. **HybridSolver Architecture**: Automatic classical/quantum routing based on problem size
3. **Portfolio Optimization**: Mean-variance portfolio optimization for risk-adjusted returns
4. **Sharpe Ratio Analysis**: Return-per-unit-risk calculations
5. **Business Context**: Real-world investment advisory use case with $100K portfolio

## Example Output

```
╔══════════════════════════════════════════════════════════════════════════════╗
║       INVESTMENT PORTFOLIO OPTIMIZATION - C# INTEROP EXAMPLE                ║
║              Using HybridSolver (Quantum-Ready Optimization)                ║
╚══════════════════════════════════════════════════════════════════════════════╝

Problem: Allocate $100,000.00 across 8 tech stocks
Objective: Maximize risk-adjusted returns (Sharpe ratio)

Running portfolio optimization with HybridSolver...
Completed in 12 ms

💡 Solver Decision: Small problem (n=8). Classical algorithms are significantly 
   faster for problems with fewer than 10 variables. Estimated classical solving 
   time: 6.40ms vs quantum: 64.00ms (speedup: 0.10x). Quantum overhead not 
   justified. Routing to classical Portfolio solver.

╔══════════════════════════════════════════════════════════════════════════════╗
║                       PORTFOLIO ALLOCATION REPORT                            ║
╚══════════════════════════════════════════════════════════════════════════════╝

ASSETS SELECTED:
────────────────────────────────────────────────────────────────────────────────
  1. MSFT   | 263.16 shares @ $380.00 = $100,000.00 (100.0%)

PORTFOLIO SUMMARY:
────────────────────────────────────────────────────────────────────────────────
  Total Invested:        $100,000.00
  Number of Holdings:    1 stocks
  Expected Annual Return: 22.00%
  Portfolio Risk (σ):    25.00%
  Sharpe Ratio:          0.88
```

## Code Highlights

### Using F# Library from C#

```csharp
using FSharp.Azure.Quantum.Classical;
using static FSharp.Azure.Quantum.CSharpBuilders;

// Define portfolio constraints
var constraints = new PortfolioSolver.Constraints(
    Budget: 100000.0,
    MinHolding: 0.0,
    MaxHolding: 100000.0
);

// Call HybridSolver (quantum-ready API)
var result = HybridSolver.solvePortfolio(
    Microsoft.FSharp.Collections.ListModule.OfSeq(assets),
    constraints,
    backend: null,
    timeout: null,
    maxCost: null
);

// Handle F# Result type in C#
if (result.IsOk)
{
    var solution = result.ResultValue;
    Console.WriteLine($"Expected Return: {solution.Result.ExpectedReturn:P}");
    Console.WriteLine($"Portfolio Risk: {solution.Result.Risk:P}");
    Console.WriteLine($"Sharpe Ratio: {solution.Result.SharpeRatio:F2}");
}
```

### Defining Assets in C#

```csharp
var stocks = new[]
{
    new PortfolioSolver.Asset(
        Symbol: "AAPL",
        ExpectedReturn: 0.18,   // 18% annual return
        Risk: 0.22,              // 22% volatility
        Price: 175.00
    ),
    new PortfolioSolver.Asset(
        Symbol: "MSFT",
        ExpectedReturn: 0.22,
        Risk: 0.25,
        Price: 380.00
    ),
    // ... more stocks
};
```

## C# Interop Features Demonstrated

### 1. F# Record Types as C# Records
F# record types translate naturally to C# with named parameters:

```csharp
// F# record: { Symbol: string; ExpectedReturn: float; Risk: float; Price: float }
// C# usage:
new PortfolioSolver.Asset(Symbol: "AAPL", ExpectedReturn: 0.18, Risk: 0.22, Price: 175.0)
```

### 2. F# Result<T, TError> Handling
F# Result types work seamlessly in C#:

```csharp
var result = HybridSolver.solvePortfolio(...);

if (result.IsOk)
{
    var solution = result.ResultValue;  // Access success value
    // Process solution
}
else
{
    var errorMessage = result.ErrorValue;  // Access error message
    Console.WriteLine($"Error: {errorMessage}");
}
```

### 3. F# Lists from C# Arrays/IEnumerables
Use `ListModule.OfSeq()` to convert C# collections to F# lists:

```csharp
var assets = new[] { /* ... */ };
var fsharpList = Microsoft.FSharp.Collections.ListModule.OfSeq(assets);
```

### 4. F# Option Types
F# `option<T>` values can be null in C#:

```csharp
HybridSolver.solvePortfolio(
    assets,
    constraints,
    backend: null,    // F# None
    timeout: null,    // F# None
    maxCost: null     // F# None
);
```

## Business Context

### Problem
An investment advisor needs to allocate $100,000 across 8 technology stocks to maximize risk-adjusted returns while managing portfolio volatility.

### Solution
Uses HybridSolver with automatic classical/quantum routing:
- **Small portfolios (< 10 assets)**: Classical quadratic programming (fast, optimal)
- **Large portfolios (50+ assets)**: Quantum solvers provide speedup potential

### Metrics
- **Expected Return**: 22.00% annual
- **Portfolio Risk**: 25.00% volatility
- **Sharpe Ratio**: 0.88 (return per unit of risk)

### Industry Impact
- **$90 trillion**: Global assets under management
- **1% Sharpe improvement**: Millions in better risk-adjusted returns
- **Enterprise use cases**: Wealth management, 401(k) optimization, pension funds

## Technical Details

### Algorithm
Mean-variance portfolio optimization balancing:
- **Maximize**: Expected portfolio return
- **Minimize**: Portfolio risk (volatility)
- **Optimize**: Sharpe ratio (return/risk)

### Complexity
- Classical: O(n²) for mean-variance optimization
- Problem size: 8 assets
- Solution time: ~10-15 milliseconds

### Quantum Advantage
For larger portfolios (50+ assets), quantum optimization may provide:
- Faster convergence
- Better handling of constraints
- Improved solution quality

## Prerequisites

- .NET SDK 10.0 or later
- FSharp.Azure.Quantum library (built from source)

## Building

```bash
# Build the library first
cd ../../../
dotnet build src/FSharp.Azure.Quantum/FSharp.Azure.Quantum.fsproj

# Run the C# example
cd examples/InvestmentPortfolio_CSharp/PortfolioExample
dotnet run
```

## Comparison with F# Version

| Feature | F# Example | C# Example |
|---------|------------|------------|
| **API Usage** | Native F# syntax | C# interop with F# library |
| **Type Safety** | F# discriminated unions | C# nullable reference types |
| **Collections** | F# lists | C# arrays converted to F# lists |
| **Error Handling** | F# Result type | F# Result type with `.IsOk` checks |
| **Performance** | Identical | Identical (no overhead) |
| **Code Style** | Functional F# | Imperative C# |

## Key Takeaways

✅ **Seamless Interop**: F# quantum libraries integrate naturally into C# applications  
✅ **No Performance Overhead**: C# interop has zero performance penalty  
✅ **Enterprise Ready**: C# shops can leverage F# quantum capabilities without rewriting code  
✅ **Natural C# Patterns**: F# types translate to idiomatic C# usage  
✅ **Quantum-Ready**: Automatic classical/quantum routing without code changes

## Related Examples

- **[InvestmentPortfolio](../InvestmentPortfolio/)** - F# implementation
- **[Kasino_CSharp](../Kasino_CSharp/)** - Another C# interop example
- **[DeliveryRouting](../DeliveryRouting/)** - TSP optimization with HybridSolver

## Further Reading

- [C# and F# Interoperability Guide](https://docs.microsoft.com/en-us/dotnet/fsharp/using-fsharp-with-csharp)
- [Mean-Variance Portfolio Theory](https://en.wikipedia.org/wiki/Modern_portfolio_theory) (Markowitz, Nobel Prize)
- [Sharpe Ratio](https://en.wikipedia.org/wiki/Sharpe_ratio) - Risk-adjusted return metric
- [HybridSolver Documentation](../../../docs/api-reference.md#hybridsolver)

---

**Last Updated**: 2025-11-29  
**Status**: ✅ Complete and tested  
**Business Value**: Demonstrates enterprise C# integration with quantum optimization for finance industry
