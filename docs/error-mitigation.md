---
layout: default
title: Error Mitigation
---

# Error Mitigation

**Reduce quantum computing errors by 30-90%** using advanced error mitigation techniques.

## Overview

Quantum computers are inherently noisy - gate errors, decoherence, and measurement errors corrupt results. Error mitigation techniques reduce these errors **without requiring error-corrected quantum hardware**, providing significant accuracy improvements on today's NISQ (Noisy Intermediate-Scale Quantum) devices.

**Available Techniques:**
- **ZNE (Zero-Noise Extrapolation)** - 30-50% error reduction, moderate cost
- **PEC (Probabilistic Error Cancellation)** - 50-80% error reduction, high cost
- **REM (Readout Error Mitigation)** - 50-90% measurement error reduction, virtually free
- **Combined Strategies** - Stack techniques for maximum accuracy

## Key Concepts

### Error Sources in Quantum Computing

**1. Gate Errors**
- Imperfect quantum gates (rotation angle errors)
- Decoherence during gate operations
- Cross-talk between qubits
- **Typical Error Rate**: 0.1-1% per gate

**2. Readout Errors**
- Measurement misclassification (|0⟩ read as |1⟩)
- State preparation errors
- **Typical Error Rate**: 1-5% per measurement

**3. Noise Accumulation**
- Errors compound with circuit depth
- Deeper circuits → more cumulative error
- **Impact**: Exponential accuracy degradation

### Error Mitigation vs Error Correction

**Error Mitigation** (Available Today):
- Post-processing techniques to reduce errors
- Works on current NISQ hardware
- No additional qubits required
- 30-90% error reduction
- **Use now** on IonQ, Rigetti, Quantinuum

**Error Correction** (Future):
- Requires many physical qubits per logical qubit
- Achieves fault-tolerant computation
- Not yet practical (needs 1000+ qubits)
- **Future technology** (5-10 years away)

---

## Zero-Noise Extrapolation (ZNE)

### What is ZNE?

ZNE reduces errors by running the circuit at **increasing noise levels**, fitting a polynomial to the results, and **extrapolating back to zero noise**.

**How It Works:**
1. Run circuit at baseline noise (1.0×)
2. Artificially increase noise (1.5×, 2.0×, 3.0×)
3. Measure expectation value at each noise level
4. Fit polynomial curve to measurements
5. Extrapolate curve to zero noise (x=0)

**Result**: Estimated zero-noise value with 30-50% error reduction.

### When to Use ZNE

**Best For:**
- Quantum chemistry (VQE for molecules)
- Optimization (QAOA for business problems)
- Expectation value measurements
- IonQ and Rigetti backends

**Not Suitable For:**
- Sampling-based algorithms (Grover's search)
- Algorithms requiring specific bitstrings (not expectation values)

### API Reference

```fsharp
open FSharp.Azure.Quantum.ZeroNoiseExtrapolation

// Configure ZNE for IonQ backend
let config = {
    Method = IdentityInsertion  // Add I·I gate pairs to increase noise
    NoiseLevels = [| 1.0; 1.5; 2.0; 3.0 |]  // Baseline + 50%, 100%, 200%
    PolynomialDegree = 2  // Quadratic extrapolation
    SamplesPerLevel = 1024
}

// Create your circuit
let vqeCircuit = circuit {
    qubits 4
    RY 0 theta1
    CNOT 0 1
    RY 1 theta2
    CNOT 1 2
    RY 2 theta3
}

// Define executor (calls real quantum backend)
let executor (circuit: Circuit) : Async<Result<float, string>> =
    async {
        // Execute circuit on IonQ/Rigetti and measure expectation value
        let! result = backend.Execute(circuit, shots = 1024)
        return Ok result.ExpectationValue
    }

// Apply ZNE
match! ZNE.mitigate vqeCircuit config executor with
| Ok result ->
    printfn "Zero-noise value: %.4f" result.ZeroNoiseValue
    printfn "R² fit quality: %.4f" result.GoodnessOfFit
    printfn "Estimated error reduction: %.1f%%" (result.ErrorReduction * 100.0)
    
    // Check fit quality
    if result.GoodnessOfFit < 0.9 then
        printfn "⚠ Warning: Poor fit quality - consider more noise levels"
    
| Error err -> eprintfn "ZNE failed: %s" err.Message
```

### Configuration Options

```fsharp
// Method 1: Identity Insertion (IonQ, Rigetti)
let ionqConfig = {
    Method = IdentityInsertion  // Insert I·I pairs (identity gates)
    NoiseLevels = [| 1.0; 1.5; 2.0 |]
    PolynomialDegree = 2
    SamplesPerLevel = 1024
}

// Method 2: Circuit Folding (All backends)
let foldingConfig = {
    Method = CircuitFolding  // Fold circuit back on itself
    NoiseLevels = [| 1.0; 2.0; 3.0 |]  // Folding factor
    PolynomialDegree = 2
    SamplesPerLevel = 2048
}

// Method 3: Pulse Stretching (Quantinuum, pulse-level access)
let pulseConfig = {
    Method = PulseStretching  // Stretch gate durations
    NoiseLevels = [| 1.0; 1.2; 1.5; 2.0 |]
    PolynomialDegree = 2
    SamplesPerLevel = 1024
}
```

### Cost Analysis

**Circuit Executions**: 
- Baseline: 1× circuit execution
- With ZNE: 3-5× circuit executions (depending on noise levels)

**Example Cost**:
- IonQ: $1 per circuit → $3-5 with ZNE
- Rigetti: $0.50 per circuit → $1.50-2.50 with ZNE

**ROI**: 30-50% error reduction for 3-5× cost = **Moderate cost, high value**

### Choosing Polynomial Degree

**Linear (degree=1)**: Fast, simple, less accurate
```fsharp
PolynomialDegree = 1  // y = a + bx
```

**Quadratic (degree=2)**: Recommended default
```fsharp
PolynomialDegree = 2  // y = a + bx + cx²
```

**Cubic (degree=3)**: Complex noise models, needs 5+ noise levels
```fsharp
PolynomialDegree = 3  // y = a + bx + cx² + dx³
NoiseLevels = [| 1.0; 1.5; 2.0; 2.5; 3.0; 4.0 |]  // Need more points
```

### Working Example

See complete example: [examples/ErrorMitigation/ZNE_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ErrorMitigation/ZNE_Example.fsx)

---

## Probabilistic Error Cancellation (PEC)

### What is PEC?

PEC reduces errors by **inverting noise channels** using quasi-probability decomposition. It actively cancels errors rather than just extrapolating.

**How It Works:**
1. Characterize noise model (single-qubit, two-qubit error rates)
2. Decompose each noisy gate into quasi-probability representation
3. Sample circuits from the decomposition (some with negative weights)
4. Combine weighted results to cancel errors

**Result**: 50-80% error reduction (2-3× accuracy improvement over unmitigated).

### When to Use PEC

**Best For:**
- Critical accuracy requirements (drug discovery, finance)
- VQE with tight convergence needs
- High-value computations justifying cost
- Shallow circuits (depth ≤ 20)

**Not Suitable For:**
- Budget-constrained applications
- Deep circuits (>50 gates) - overhead too high
- Exploratory/prototyping work

### API Reference

```fsharp
open FSharp.Azure.Quantum.ProbabilisticErrorCancellation

// Define noise model (measured from hardware)
let noiseModel = {
    SingleQubitDepolarizing = 0.001  // 0.1% error per gate
    TwoQubitDepolarizing = 0.01      // 1.0% error per CNOT
    ReadoutError = 0.02              // 2% measurement error
}

// Configure PEC
let config = {
    NoiseModel = noiseModel
    NumSamples = 10000  // More samples = better accuracy but higher cost
    Precision = 0.001   // Target precision
    MaxCircuitSamples = 100000  // Safety limit
}

// Create circuit
let h2Circuit = circuit {
    qubits 2
    RY 0 theta
    CNOT 0 1
    RY 1 theta
}

// Define executor
let executor (circuit: Circuit) : Async<Result<float, string>> =
    async {
        let! result = backend.Execute(circuit, shots = 1024)
        return Ok result.ExpectationValue
    }

// Apply PEC
match! PEC.mitigate h2Circuit config executor with
| Ok result ->
    printfn "Mitigated value: %.4f" result.MitigatedValue
    printfn "Unmitigated value: %.4f" result.UnmitigatedValue
    printfn "Error reduction: %.1f%%" (result.ErrorReduction * 100.0)
    printfn "Circuit samples used: %d" result.SamplesUsed
    printfn "Overhead factor: %.1fx" result.OverheadFactor
    
| Error err -> eprintfn "PEC failed: %s" err.Message
```

### Noise Model Characterization

**Option 1: Use Published Values**
```fsharp
// IonQ Aria (typical values)
let ionqNoise = {
    SingleQubitDepolarizing = 0.0003  // 0.03% error
    TwoQubitDepolarizing = 0.005      // 0.5% error
    ReadoutError = 0.01               // 1% readout error
}

// Rigetti Aspen (typical values)
let rigettiNoise = {
    SingleQubitDepolarizing = 0.001   // 0.1% error
    TwoQubitDepolarizing = 0.01       // 1% error
    ReadoutError = 0.02               // 2% readout error
}
```

**Option 2: Measure Your Own**
```fsharp
// Run randomized benchmarking circuits
match! PEC.characterizeNoiseModel backend with
| Ok measured ->
    printfn "Measured noise model:"
    printfn "  Single-qubit: %.4f" measured.SingleQubitDepolarizing
    printfn "  Two-qubit: %.4f" measured.TwoQubitDepolarizing
    printfn "  Readout: %.4f" measured.ReadoutError
| Error err -> eprintfn "Characterization failed: %s" err.Message
```

### Cost Analysis

**Circuit Executions**: 
- Baseline: 1× circuit execution
- With PEC: 10-100× circuit executions (quasi-probability sampling)

**Example Cost**:
- IonQ: $1 per circuit → $10-100 with PEC
- Rigetti: $0.50 per circuit → $5-50 with PEC

**ROI**: 50-80% error reduction for 10-100× cost = **High cost, critical use cases only**

### Overhead Estimation

**Overhead depends on:**
- Circuit depth (deeper = higher overhead)
- Noise levels (noisier = higher overhead)
- Target precision (tighter = higher overhead)

**Typical Overheads:**
- Shallow circuits (depth ≤ 10): 10-30×
- Medium circuits (depth 10-20): 30-100×
- Deep circuits (depth > 20): 100-1000× (impractical)

### Working Example

See complete example: [examples/ErrorMitigation/PEC_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ErrorMitigation/PEC_Example.fsx)

---

## Readout Error Mitigation (REM)

### What is REM?

REM reduces measurement errors by calibrating a **confusion matrix** that maps true states to measured states, then applying the inverse transformation.

**How It Works:**
1. **Calibration** (one-time): Prepare known states (|00⟩, |01⟩, |10⟩, |11⟩)
2. Measure each state many times to build confusion matrix
3. Invert matrix to get correction transformation
4. **Runtime**: Apply inverse matrix to correct all measurements

**Result**: 50-90% reduction in readout errors, virtually **free after calibration**.

### When to Use REM

**Best For:**
- **ALWAYS!** It's the cheapest error mitigation technique
- High-shot-count applications (≥1000 shots)
- Sampling-based algorithms (Grover's, QAOA sampling)
- Any application on real quantum hardware

**Not Suitable For:**
- LocalBackend (already perfect readout)
- Low-shot applications (<100 shots)

### API Reference

```fsharp
open FSharp.Azure.Quantum.ReadoutErrorMitigation

// Configure REM
let config = {
    CalibrationShots = 10000  // Shots per calibration state
    ConfidenceLevel = 0.95    // 95% confidence intervals
    ClipNegative = true       // Clip negative counts to 0
}

// Step 1: Calibrate (one-time per backend session)
match! REM.calibrate backend config with
| Ok calibration ->
    printfn "Calibration complete!"
    printfn "Confusion matrix:"
    printfn "  P(measure 0|prepare 0): %.4f" calibration.ConfusionMatrix.[0,0]
    printfn "  P(measure 1|prepare 0): %.4f" calibration.ConfusionMatrix.[0,1]
    printfn "  P(measure 0|prepare 1): %.4f" calibration.ConfusionMatrix.[1,0]
    printfn "  P(measure 1|prepare 1): %.4f" calibration.ConfusionMatrix.[1,1]
    
    // Step 2: Run your circuit
    let circuit = circuit {
        qubits 2
        H 0
        CNOT 0 1
    }
    
    let! rawCounts = backend.Execute(circuit, shots = 10000)
    
    // Step 3: Apply correction
    match REM.correct calibration rawCounts with
    | Ok corrected ->
        printfn "\nRaw counts: %A" rawCounts
        printfn "Corrected counts: %A" corrected.Counts
        printfn "Error reduction: %.1f%%" (corrected.ErrorReduction * 100.0)
    | Error err -> eprintfn "Correction failed: %s" err.Message
    
| Error err -> eprintfn "Calibration failed: %s" err.Message
```

### Multi-Qubit Calibration

**For n qubits, need 2ⁿ calibration states:**

```fsharp
// 1 qubit: 2 states (|0⟩, |1⟩)
let! cal1 = REM.calibrate backend config

// 2 qubits: 4 states (|00⟩, |01⟩, |10⟩, |11⟩)
let! cal2 = REM.calibrate backend { config with NumQubits = 2 }

// 3 qubits: 8 states (|000⟩, |001⟩, ..., |111⟩)
let! cal3 = REM.calibrate backend { config with NumQubits = 3 }
```

**Calibration Cost**:
- 1 qubit: 2 circuits
- 2 qubits: 4 circuits
- 3 qubits: 8 circuits
- 4 qubits: 16 circuits
- **Scales exponentially** - practical for ≤10 qubits

### Handling Negative Counts

**Problem**: Matrix inversion can produce negative counts (unphysical)

**Solutions**:

**Option 1: Clip to Zero** (default)
```fsharp
ClipNegative = true
// Negative counts → 0 (simple, conservative)
```

**Option 2: Redistribute**
```fsharp
ClipNegative = false
RedistributeNegative = true
// Negative counts redistributed to positive states (preserves total)
```

**Option 3: Allow Negative** (advanced)
```fsharp
ClipNegative = false
// Keep negative counts (for theoretical analysis)
```

### Cost Analysis

**Circuit Executions**: 
- Calibration: 2ⁿ circuits (one-time)
- Runtime: **0× overhead** (pure post-processing)

**Example Cost**:
- Calibration (3 qubits): 8 circuits = $8 (one-time)
- Per-circuit cost: **$0** (free!)

**ROI**: 50-90% error reduction for free after calibration = **Best value in error mitigation!**

### Working Example

See complete example: [examples/ErrorMitigation/REM_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ErrorMitigation/REM_Example.fsx)

---

## Combined Strategies

### Why Combine Techniques?

Error mitigation techniques target different error sources:
- **ZNE**: Gate errors
- **PEC**: Gate errors (more aggressive)
- **REM**: Readout errors

**Combining techniques multiplicatively reduces errors.**

### Recommended Combinations

#### 1. REM + ZNE (Best Value)

**Cost**: Low-Medium (3-5× overhead)  
**Accuracy**: 60-80% total error reduction  
**Use For**: Most applications on real hardware

```fsharp
// Step 1: Calibrate REM (one-time)
let! remCal = REM.calibrate backend remConfig

// Step 2: Run circuit with ZNE
match! ZNE.mitigate circuit zneConfig (fun c -> 
    async {
        let! rawCounts = backend.Execute(c, shots = 1024)
        
        // Step 3: Apply REM correction
        let! corrected = REM.correct remCal rawCounts
        return Ok corrected.ExpectationValue
    }
) with
| Ok result ->
    printfn "Mitigated value: %.4f" result.ZeroNoiseValue
| Error err -> eprintfn "Error: %s" err.Message
```

**Benefits**:
- REM is free after calibration
- ZNE adds moderate cost (3-5×)
- Targets both gate and readout errors
- **Recommended default for production**

#### 2. REM + PEC (Maximum Accuracy)

**Cost**: High (10-100× overhead)  
**Accuracy**: 70-95% total error reduction  
**Use For**: Critical high-accuracy applications

```fsharp
// Step 1: Calibrate REM
let! remCal = REM.calibrate backend remConfig

// Step 2: Run circuit with PEC
match! PEC.mitigate circuit pecConfig (fun c ->
    async {
        let! rawCounts = backend.Execute(c, shots = 1024)
        
        // Step 3: Apply REM correction
        let! corrected = REM.correct remCal rawCounts
        return Ok corrected.ExpectationValue
    }
) with
| Ok result ->
    printfn "Mitigated value: %.4f" result.MitigatedValue
| Error err -> eprintfn "Error: %s" err.Message
```

**Benefits**:
- Maximum error reduction possible
- Targets all error sources
- **Use only when accuracy justifies cost**

#### 3. All Three (Experimental)

**Cost**: Very High (30-500× overhead)  
**Accuracy**: Up to 95%+ error reduction  
**Use For**: Research, extremely critical calculations

```fsharp
open FSharp.Azure.Quantum.ErrorMitigation.Combined

// Combined strategy configuration
let strategy = {
    UseZNE = true
    UsePEC = true
    UseREM = true
    ZNEConfig = zneConfig
    PECConfig = pecConfig
    REMConfig = remConfig
}

match! Combined.mitigate circuit strategy backend with
| Ok result ->
    printfn "Final mitigated value: %.4f" result.FinalValue
    printfn "Total error reduction: %.1f%%" (result.TotalErrorReduction * 100.0)
    printfn "Total overhead: %.1fx" result.TotalOverhead
| Error err -> eprintfn "Error: %s" err.Message
```

### Strategy Selection Guide

| Application | Recommended Strategy | Cost | Accuracy Improvement |
|-------------|---------------------|------|----------------------|
| **Prototyping** | REM only | Free | 50-70% |
| **Production** | REM + ZNE | Low | 60-80% |
| **High-value** | REM + PEC | High | 70-95% |
| **Research** | All three | Very High | 80-95%+ |

---

## Performance Comparison

### Error Reduction Effectiveness

| Technique | Gate Errors | Readout Errors | Cost | Recommendation |
|-----------|-------------|----------------|------|----------------|
| **ZNE** | 30-50% | 0% | 3-5× | Good value |
| **PEC** | 50-80% | 0% | 10-100× | Critical use only |
| **REM** | 0% | 50-90% | Free | Always use |
| **REM+ZNE** | 30-50% | 50-90% | 3-5× | **Best default** |
| **REM+PEC** | 50-80% | 50-90% | 10-100× | Maximum accuracy |

### Circuit Depth Limits

| Technique | Shallow (≤10 gates) | Medium (10-30 gates) | Deep (>30 gates) |
|-----------|---------------------|----------------------|------------------|
| **ZNE** | ✅ Excellent | ✅ Good | ⚠️ Moderate |
| **PEC** | ✅ Excellent | ⚠️ Expensive | ❌ Impractical |
| **REM** | ✅ Excellent | ✅ Excellent | ✅ Excellent |

---

## Troubleshooting

### Common Issues

#### 1. Poor ZNE Fit Quality (R² < 0.9)

**Symptoms:** Low goodness-of-fit score

**Solutions:**
- Add more noise levels (try 5-7 instead of 3)
- Use higher polynomial degree (cubic instead of quadratic)
- Increase samples per level (2048 instead of 1024)
- Check if noise model is appropriate for backend

#### 2. PEC Overhead Too High

**Symptoms:** >100× overhead, cost prohibitive

**Solutions:**
- Reduce circuit depth (simplify algorithm)
- Use ZNE instead of PEC
- Split computation into shallower sub-circuits
- Consider if accuracy requirement justifies cost

#### 3. REM Produces Negative Counts

**Symptoms:** Unphysical negative counts after correction

**Solutions:**
- Enable `ClipNegative = true` (default, safest)
- Increase calibration shots (10,000+)
- Check if confusion matrix is well-conditioned
- Use `RedistributeNegative` option

#### 4. Combined Strategies Don't Improve Accuracy

**Symptoms:** Mitigation makes results worse

**Solutions:**
- Check calibration quality (REM confusion matrix)
- Verify noise model accuracy (for PEC)
- Ensure sufficient samples (ZNE/PEC)
- May be dominated by other errors (try different technique)

## Working Examples

See complete, runnable examples in `examples/ErrorMitigation/`:

- **[ZNE_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ErrorMitigation/ZNE_Example.fsx)** - Zero-Noise Extrapolation demo
- **[PEC_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ErrorMitigation/PEC_Example.fsx)** - Probabilistic Error Cancellation demo
- **[REM_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ErrorMitigation/REM_Example.fsx)** - Readout Error Mitigation demo
- **[CombinedStrategy_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ErrorMitigation/CombinedStrategy_Example.fsx)** - Combining multiple techniques

## See Also

- [Getting Started Guide](getting-started) - Installation and setup
- [Backend Switching](backend-switching) - Cloud quantum backends
- [Quantum Chemistry API](QuantumChemistry-API) - VQE with error mitigation
- [API Reference](api-reference) - Complete API documentation

## References

- **Zero-Noise Extrapolation**: [Temme et al., PRL (2017)](https://arxiv.org/abs/1612.02058)
- **Probabilistic Error Cancellation**: [Temme et al., PRL (2017)](https://arxiv.org/abs/1612.02058)
- **Readout Error Mitigation**: [Maciejewski et al., Quantum (2020)](https://arxiv.org/abs/1907.08518)
- **Error Mitigation Review**: [Endo et al., JPSJ (2021)](https://arxiv.org/abs/1808.00709)

---

**Last Updated**: December 2025
