# Quantum Pattern Matcher Examples

Find items in large search spaces that match expensive criteria using Grover's algorithm for O(√N) speedup.

## Examples

### 1. Database Configuration Optimization (`ConfigurationOptimizer.fsx`)

Find optimal database configurations from 256 combinations achieving performance targets.

**Run:**
```bash
dotnet fsi ConfigurationOptimizer.fsx
```

**Use Cases:**
- Database tuning (cache size, pool size, timeouts)
- System configuration optimization
- Compiler flag selection

**Quantum Advantage:**
- Classical: 256 benchmarks × 45 sec = 192 minutes
- Quantum: √256 = 16 benchmarks × 45 sec = 12 minutes
- **16× speedup!**

### 2. ML Hyperparameter Tuning

Find optimal neural network hyperparameters from 128 combinations achieving >95% accuracy.

**Use Cases:**
- Learning rate, batch size, layer count selection
- Model architecture search
- Training parameter optimization

**Quantum Advantage:**
- Classical: 128 × 15 min = 32 hours
- Quantum: √128 ≈ 11 × 15 min = 2.75 hours
- **11× speedup saves days of GPU time!**

### 3. Feature Selection for ML

Select best feature subsets from 256 combinations (2^8 features) for ML models.

**Use Cases:**
- Feature engineering for ML
- Dimensionality reduction
- Model simplification

**Quantum Advantage:**
- Classical: 256 model trainings
- Quantum: √256 = 16 model trainings
- **16× fewer training runs!**

## When to Use

✅ **Good for:**
- Configuration optimization (databases, compilers, systems)
- Hyperparameter tuning (ML, simulation parameters)
- Feature selection (ML feature engineering)
- Search spaces: 100-10,000 candidates
- **Expensive evaluation (10+ seconds per candidate)**

❌ **Not suitable for:**
- Fast evaluations (<1 second) - classical is better
- Very small search spaces (<50 items)
- Problems with known structure
- Need exact optimum (use optimization algorithms instead)

## Quantum Advantage

**Grover's algorithm:** O(√N) vs O(N) classical exhaustive search

**Best for:** Expensive evaluation + large search space

**Example:** 256 configs × 60 sec each
- Classical: 256 × 60 sec = 4 hours
- Quantum: 16 × 60 sec = 16 minutes
- **15× speedup saves hours of compute!**

## API Usage

```fsharp
open FSharp.Azure.Quantum.QuantumPatternMatcher
open FSharp.Azure.Quantum.Core.BackendAbstraction

let localBackend = LocalBackend() :> IQuantumBackend

let problem = patternMatcher {
    searchSpace allConfigurations  // 128 configs
    matchPattern (fun config ->
        let perf = runBenchmark config  // Expensive: 10 seconds
        perf.Throughput > 1000.0 && perf.Latency < 50.0
    )
    findTop 10
    backend localBackend
    shots 1000
}

match solve problem with
| Ok result -> printfn "Found %d matches" result.Matches.Length
| Error err -> printfn "Error: %s" err.Message
```

## Related Examples

- **Constraint Solving:** Use `QuantumConstraintSolverBuilder` for CSP problems
- **Tree Search:** Use `QuantumTreeSearchBuilder` for game AI
- **Graph Problems:** Use `GraphColoringBuilder` for coloring problems
