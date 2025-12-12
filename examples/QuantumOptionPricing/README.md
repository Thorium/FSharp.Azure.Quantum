# Quantum Option Pricing Example

Demonstrates quantum Monte Carlo option pricing using FSharp.Azure.Quantum.

## Overview

This example shows how to use quantum computing to price financial derivatives with **quadratic speedup** over classical Monte Carlo methods.

### Quantum Advantage

- **Classical Monte Carlo**: O(1/ε²) samples → 10,000 samples for 1% accuracy
- **Quantum Monte Carlo**: O(1/ε) queries → 100 queries for 1% accuracy
- **Result**: **100x speedup** for same accuracy!

## Features

1. **Möttönen State Preparation** - Exact amplitude encoding of GBM distribution
2. **Grover Amplitude Estimation** - Quantum amplitude estimation algorithm
3. **Multiple Option Types** - European, Asian (call and put)
4. **Production Validation** - Comprehensive input validation
5. **LocalBackend Support** - Run on quantum simulator
6. **Azure Quantum Ready** - Deploy to real quantum hardware

## What's Demonstrated

### Example 1: European Call Option
Price a standard European call option with quantum simulation.

### Example 2: Put-Call Comparison
Compare call vs put prices and verify put-call parity.

### Example 3: Moneyness Analysis
Price options at different strike prices (ITM, ATM, OTM).

### Example 4: Volatility Smile
Demonstrate how volatility affects option prices.

### Example 5: Input Validation
Show production-ready error handling.

### Example 6: Asian Options
Price path-dependent options with averaging.

## Running the Example

```bash
# Using .NET Interactive (recommended)
dotnet fsi QuantumOptionPricing.fsx

# Or from examples directory
cd examples/QuantumOptionPricing
dotnet fsi QuantumOptionPricing.fsx
```

## Expected Output

```
╔═══════════════════════════════════════════════════════════════╗
║   Quantum Monte Carlo Option Pricing                         ║
║   Using FSharp.Azure.Quantum                                  ║
╚═══════════════════════════════════════════════════════════════╝

═══ Example 1: European Call Option ═══

Market Parameters:
  Spot Price (S₀):    $100.00
  Strike Price (K):   $105.00
  Risk-free Rate (r): 5.0%
  Volatility (σ):     20.0%
  Time to Expiry (T): 1.0 year

Using LocalBackend (quantum simulator)...
Running quantum Monte Carlo...

✓ Success!

RESULTS:
  Option Price:          $X.XXXX
  Confidence Interval:   ±$X.XXXX
  Price Range:           $X.XXXX - $X.XXXX
  Qubits Used:           6 (2^6 = 64 price levels)
  Method:                Quantum Monte Carlo (Möttönen + Grover)
  Quantum Speedup:       XXXx

...
```

## Mathematical Background

### Black-Scholes Model

Option price under geometric Brownian motion (GBM):

```
C = E[e^(-rT) · max(S_T - K, 0)]

where:
  S_T = S_0 * exp((r - σ²/2)T + σ√T * Z)
  Z ~ N(0,1)
```

### Quantum Encoding

1. **State Preparation**: Encode log-normal distribution using Möttönen
   ```
   |ψ⟩ = ∑ᵢ √p(Sᵢ) |i⟩
   ```

2. **Oracle**: Mark "in-the-money" states (simplified MSB threshold)

3. **Amplitude Estimation**: Grover iterations to estimate probability

4. **Pricing**: Extract expected payoff and discount

## Code Structure

```fsharp
// 1. Setup backend
let backend = LocalBackend.LocalBackend() :> IQuantumBackend

// 2. Price option
let! result = OptionPricing.priceEuropeanCall 
    100.0  // spot
    105.0  // strike
    0.05   // rate
    0.2    // volatility
    1.0    // expiry
    backend

// 3. Handle result
match result with
| Ok price -> printfn "Price: $%.2f" price.Price
| Error err -> printfn "Error: %A" err
```

## Implementation Details

### Advantages
- ✅ Exact GBM encoding (Möttönen state preparation)
- ✅ Production-ready validation
- ✅ RULE1 compliant (backend always required)
- ✅ Multi-backend support (Local, IonQ, Rigetti)
- ✅ Type-safe error handling

### Current Limitations
- ⚠️ Payoff oracle uses MSB approximation (not exact comparison)
- ⚠️ Works best when strike ≈ median price
- ⚠️ Limited to 2-10 qubits (4-1024 price levels)
- ⚠️ Simplified amplitude extraction (not full QAE)

### Future Enhancements
- Exact comparison oracle using QuantumArithmetic
- Full Quantum Amplitude Estimation (QAE)
- More exotic derivatives (barriers, lookbacks)
- Stochastic volatility models (Heston)

## Dependencies

- **FSharp.Azure.Quantum** - Main library
- **.NET SDK** - Runtime

## Performance

| Accuracy | Classical Samples | Quantum Queries | Speedup |
|----------|------------------|-----------------|---------|
| 10%      | 100              | 10              | 10x     |
| 1%       | 10,000           | 100             | 100x    |
| 0.1%     | 1,000,000        | 1,000           | 1000x   |

*Theoretical speedup. Actual performance depends on quantum hardware noise.*

## References

1. **Rebentrost et al.** (2018): "Quantum computational finance: Monte Carlo pricing of financial derivatives" - [arXiv:1805.00109](https://arxiv.org/abs/1805.00109)

2. **Möttönen & Vartiainen** (2004): "Quantum circuits for general multiqubit gates"

3. **Black-Scholes** (1973): Original option pricing model

## Related Examples

- `LinearSystemSolver/HHLAlgorithm.fsx` - HHL algorithm for solving linear systems
- `QuantumChemistry/` - Quantum simulation examples
- `InvestmentPortfolio/` - Portfolio optimization

## Support

For issues or questions:
- GitHub Issues: [FSharp.Azure.Quantum](https://github.com/yourusername/FSharp.Azure.Quantum)
- Documentation: See library docs
- Community: F# Slack, Azure Quantum forums

## License

See repository license.
