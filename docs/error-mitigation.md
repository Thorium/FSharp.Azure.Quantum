---
layout: default
title: Error Mitigation
---

# Error Mitigation

**Reduce the effect of hardware noise on quantum results** using zero-noise extrapolation, probabilistic error cancellation and readout error mitigation.

## Overview

Quantum computers are inherently noisy - gate errors, decoherence, and measurement errors corrupt results. Error mitigation techniques reduce these errors **without requiring error-corrected quantum hardware**, which makes them useful on today's NISQ (Noisy Intermediate-Scale Quantum) devices.

**Available Techniques:**
- **ZNE (Zero-Noise Extrapolation)** - `ZeroNoiseExtrapolation` module; typically 30-50% error reduction, moderate cost
- **PEC (Probabilistic Error Cancellation)** - `ProbabilisticErrorCancellation` module; typically 50-80% error reduction, high cost
- **REM (Readout Error Mitigation)** - `ReadoutErrorMitigation` module; typically 50-90% reduction of measurement errors, cheap after calibration
- **Strategy selection** - `ErrorMitigationStrategy` module recommends a technique (or a combination) from circuit size, budget and accuracy target

The percentages are typical ranges reported in the literature, not guarantees: the actual improvement depends on the circuit, the device and how well the noise matches each technique's assumptions.

All three techniques work on `CircuitBuilder.Circuit` values and take an **executor** function that you supply. The executor runs a circuit on whatever backend you choose (cloud hardware, or the local noisy simulator used in the examples below) and returns either an expectation value (ZNE, PEC) or a measurement histogram (REM). This keeps the mitigation code independent of the backend.

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
- Costs extra circuit executions instead
- **Use now** on IonQ, Rigetti, Quantinuum

**Error Correction** (Future):
- Requires many physical qubits per logical qubit
- Achieves fault-tolerant computation
- Not yet practical at useful scale

---

## A Noisy Executor for the Examples

The examples on this page use `NoisyLocalBackend`, a density-matrix simulator that applies a depolarizing channel after every gate, so the mitigation has real noise to work against. `Primitives.observe` runs a circuit on a backend and returns the expectation value of a Pauli observable; here the observable is Z⊗Z.

```fsharp
open System.Numerics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends.DensityMatrixSimulator

// 1% depolarizing error per single-qubit gate, 2% per two-qubit gate
let backend = NoisyLocalBackend(depolarizing 0.01 0.02) :> IQuantumBackend

// Observable Z⊗Z on two qubits
let zz : TrotterSuzuki.PauliHamiltonian =
    { NumQubits = 2
      Terms = [ { Operators = [| 'Z'; 'Z' |]; Coefficient = Complex.One } ] }

// Executor for ZNE and PEC: circuit -> expectation value
let executor (c: Circuit) : Async<Result<float, string>> =
    async { return Primitives.observe backend c zz |> Result.mapError (fun e -> e.Message) }

// A small VQE-style ansatz to mitigate
let theta = 0.4

let vqeCircuit =
    circuit {
        qubits 2
        RY 0 theta
        CNOT 0 1
        RY 1 theta
    }
```

On real hardware, replace `backend` with a cloud backend (see [Backend Switching](backend-switching)) and keep the rest: `Primitives.observe` then estimates ⟨Z⊗Z⟩ from measured shots, one job per commuting group of terms, so every executor call is at least one billed job. The noisy simulator holds a full density matrix, so it is meant for small circuits (it refuses more than 8 qubits).

---

## Zero-Noise Extrapolation (ZNE)

### What is ZNE?

ZNE reduces errors by running the circuit at **increasing noise levels**, fitting a polynomial to the results, and **extrapolating back to zero noise**.

**How It Works:**
1. Run the circuit at baseline noise (1.0×)
2. Artificially increase noise (for example 1.5× and 2.0×) by making the circuit longer
3. Measure the expectation value at each noise level
4. Fit a polynomial curve to the measurements (least squares)
5. Extrapolate the curve to zero noise (the constant term of the polynomial)

### When to Use ZNE

**Best For:**
- Quantum chemistry (VQE for molecules)
- Optimization (QAOA for business problems)
- Expectation value measurements

**Not Suitable For:**
- Sampling-based algorithms (Grover's search)
- Algorithms requiring specific bitstrings (not expectation values)

### API Reference

```fsharp
open FSharp.Azure.Quantum.ZeroNoiseExtrapolation

// Baseline plus two amplified noise levels, quadratic fit
let zneConfig = defaultIonQConfig |> withPolynomialDegree 2

match ZeroNoiseExtrapolation.mitigate vqeCircuit zneConfig executor |> Async.RunSynchronously with
| Ok result ->
    printfn "Zero-noise value: %.4f" result.ZeroNoiseValue
    printfn "R² fit quality: %.4f" result.GoodnessOfFit

    for (noiseLevel, value) in result.MeasuredValues do
        printfn "  %.2fx noise -> %.4f" noiseLevel value

    // Check fit quality
    if result.GoodnessOfFit < 0.9 then
        printfn "Warning: poor fit quality - consider more noise levels"
| Error msg -> eprintfn "ZNE failed: %s" msg
```

`mitigate` runs the executor once per noise level (in parallel) and returns `Async<Result<ZNEResult, string>>`. `ZNEResult` holds `ZeroNoiseValue`, `MeasuredValues` (noise level, value pairs), `PolynomialCoefficients` and `GoodnessOfFit` (R²). It does not know the ideal value, so it cannot report an error reduction; compare against a known reference yourself if you have one.

### Configuration Options

`ZNEConfig` has three fields: `NoiseScalings` (one `NoiseScaling` per noise level), `PolynomialDegree` and `MinSamples`.

```fsharp
// Identity insertion: IdentityInsertion r inserts X·X pairs (an identity) to lengthen the circuit.
// The noise level recorded for the fit is 1 + r.
let identityConfig : ZNEConfig =
    { NoiseScalings = [ IdentityInsertion 0.0; IdentityInsertion 0.5; IdentityInsertion 1.0 ]
      PolynomialDegree = 2
      MinSamples = 1024 }

// Pulse stretching: PulseStretching s uses s itself as the noise level.
let stretchConfig =
    defaultRigettiConfig
    |> withNoiseScalings [ PulseStretching 1.0; PulseStretching 1.5; PulseStretching 2.0; PulseStretching 2.5 ]
```

- `defaultIonQConfig` uses `IdentityInsertion 0.0 / 0.5 / 1.0`; `defaultRigettiConfig` uses `PulseStretching 1.0 / 1.5 / 2.0`. Both use degree 2 and `MinSamples = 1024`.
- The library works at gate level and has no pulse control, so `PulseStretching s` is realised digitally: it inserts identity pairs so the gate count grows by the factor `s`. Both scalings therefore run on any gate-based backend.
- `mitigate` does not pass `MinSamples` to your executor; the executor decides how many shots to use.

### Cost Analysis

**Circuit Executions**:
- Baseline: 1× circuit execution
- With ZNE: one execution per noise level (3 with the default configurations)

**Example Cost** (illustrative prices):
- $1 per circuit → about $3 with three noise levels

### Choosing Polynomial Degree

The fit needs at least `degree + 1` noise levels; with fewer, `mitigate` returns an `Error`.

```fsharp
// Linear (degree 1): E(λ) = a + bλ. Fast, simple, less accurate
let linear = zneConfig |> withPolynomialDegree 1

// Quadratic (degree 2): E(λ) = a + bλ + cλ². Recommended default
let quadratic = zneConfig |> withPolynomialDegree 2

// Cubic (degree 3): needs at least 4 noise levels; more points make the fit more stable
let cubic =
    zneConfig
    |> withPolynomialDegree 3
    |> withNoiseScalings [ for s in [ 1.0; 1.5; 2.0; 2.5; 3.0 ] -> PulseStretching s ]
```

### Working Example

See complete example: [examples/ErrorMitigation/ZNE_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ErrorMitigation/ZNE_Example.fsx)

---

## Probabilistic Error Cancellation (PEC)

### What is PEC?

PEC reduces errors by **inverting noise channels** using quasi-probability decomposition. It actively cancels errors rather than just extrapolating.

**How It Works:**
1. Describe the noise with a depolarizing noise model (single-qubit and two-qubit error rates)
2. Decompose each noisy gate into a quasi-probability mixture of the gate followed by Pauli corrections (some weights are negative)
3. Sample circuits from the decomposition (Monte Carlo)
4. Combine the weighted results to cancel the modelled noise

### When to Use PEC

**Best For:**
- Critical accuracy requirements
- VQE with tight convergence needs
- High-value computations justifying cost
- Shallow circuits

**Not Suitable For:**
- Budget-constrained applications
- Deep circuits - sampling overhead grows with every gate
- Noise that is far from depolarizing (the correction is only as good as the noise model)

### API Reference

```fsharp
open FSharp.Azure.Quantum.ProbabilisticErrorCancellation

// Depolarizing noise model (here: matches the simulator above)
let noiseModel : ProbabilisticErrorCancellation.NoiseModel =
    { SingleQubitDepolarizing = 0.01 // 1% error per single-qubit gate
      TwoQubitDepolarizing = 0.02    // 2% error per two-qubit gate
      ReadoutError = 0.0 }           // not used by PEC; handle readout with REM

let pecConfig : PECConfig =
    { NoiseModel = noiseModel
      Samples = 1000   // Monte Carlo samples: more = lower variance, higher cost
      Seed = Some 42 } // reproducible sampling

match ProbabilisticErrorCancellation.mitigate vqeCircuit pecConfig executor |> Async.RunSynchronously with
| Ok result ->
    printfn "Corrected value:   %.4f" result.CorrectedExpectation
    printfn "Uncorrected value: %.4f" result.UncorrectedExpectation
    printfn "Relative change:   %.1f%%" (result.ErrorReduction * 100.0)
    printfn "Samples used:      %d" result.SamplesUsed
    printfn "Overhead:          %.0fx" result.Overhead
| Error msg -> eprintfn "PEC failed: %s" msg
```

`mitigate` returns `Async<Result<PECResult, string>>`. `ErrorReduction` is the relative difference between the corrected and uncorrected values, `|corrected − uncorrected| / |uncorrected|`; without the ideal value the library cannot measure the true error reduction. `Overhead` is the number of sampled circuits (`Samples`); the baseline adds one more execution.

### Noise Model Characterization

The noise model comes from you. The library does not characterize a device; take the error rates from the provider's published calibration data or from your own benchmarking runs.

```fsharp
// Illustrative values - check the provider's current calibration data
let trappedIonNoise : ProbabilisticErrorCancellation.NoiseModel =
    { SingleQubitDepolarizing = 0.0003 // 0.03% error
      TwoQubitDepolarizing = 0.005     // 0.5% error
      ReadoutError = 0.01 }

let superconductingNoise : ProbabilisticErrorCancellation.NoiseModel =
    { SingleQubitDepolarizing = 0.001  // 0.1% error
      TwoQubitDepolarizing = 0.01      // 1% error
      ReadoutError = 0.02 }
```

### Cost Analysis

**Circuit Executions**:
- Baseline: 1× circuit execution
- With PEC: `Samples` + 1 executions (10-100× is common)

**Example Cost** (illustrative prices):
- $1 per circuit → $10-100 with PEC

### Overhead Estimation

**Overhead depends on:**
- Circuit depth (deeper = higher overhead)
- Noise levels (noisier = higher overhead)
- Target precision (tighter = higher overhead)

Each gate's decomposition has a normalization factor Σ|pᵢ| > 1, and the variance of the estimate grows with the product of these factors over all gates, so the number of samples needed for a given precision grows exponentially with circuit depth.

### Working Example

See complete example: [examples/ErrorMitigation/PEC_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ErrorMitigation/PEC_Example.fsx)

---

## Readout Error Mitigation (REM)

### What is REM?

REM reduces measurement errors by calibrating a **confusion matrix** that maps prepared states to measured states, then applying the inverse transformation to measured histograms.

**How It Works:**
1. **Calibration** (one-time): prepare every basis state (|00⟩, |01⟩, |10⟩, |11⟩ for 2 qubits)
2. Measure each state many times to build the confusion matrix `M[measured, prepared]`
3. Invert the matrix (LU decomposition)
4. **Runtime**: apply the inverse matrix to correct each measured histogram

### When to Use REM

**Best For:**
- Nearly every run on real hardware - it is the cheapest technique
- High-shot-count applications (≥1000 shots)
- Sampling-based algorithms (Grover's, QAOA sampling)

**Not Suitable For:**
- The noiseless `LocalBackend` (its readout is already perfect)
- Low-shot applications (<100 shots)
- More than 10 qubits (calibration is limited to 1-10 qubits)

### API Reference

REM executors take a circuit and a shot count and return a histogram. `ReadoutErrorMitigation` reads bitstrings with the **highest qubit first** (the rightmost character is qubit 0), while `Primitives.sample` writes qubit 0 first, so the executor below reverses each key. On a cloud backend `Primitives.sample` returns the job's own counts and accepts only the shot count the backend was created with, so create it with the shots REM asks for (`withCalibrationShots`), or one backend per shot count.

```fsharp
open FSharp.Azure.Quantum.ReadoutErrorMitigation

// REM executor: circuit -> shots -> histogram (keys with the highest qubit first)
let sampleExecutor (c: Circuit) (shots: int) : Async<Result<Map<string, int>, string>> =
    async {
        return
            Primitives.sample backend c shots
            |> Result.map (fun histogram ->
                histogram
                |> Map.toList
                |> List.map (fun (bits, count) -> System.String(Array.rev (bits.ToCharArray())), count)
                |> Map.ofList)
            |> Result.mapError (fun e -> e.Message)
    }

// Configure REM (defaultConfig: 10,000 shots, 95% confidence, clip negatives, 1% filter)
let remConfig = defaultConfig |> withCalibrationShots 10000

// Step 1: Calibrate (one-time per backend session): 2^n calibration circuits
match measureCalibrationMatrix "noisy-local" 2 remConfig sampleExecutor |> Async.RunSynchronously with
| Error msg -> eprintfn "Calibration failed: %s" msg
| Ok calibration ->
    // Matrix.[measured, prepared]; each column sums to 1
    printfn "P(measure 00 | prepared 00): %.4f" calibration.Matrix.[0, 0]
    printfn "P(measure 01 | prepared 00): %.4f" calibration.Matrix.[1, 0]

    // Step 2: Run your circuit
    let bell =
        circuit {
            qubits 2
            H 0
            CNOT 0 1
        }

    match sampleExecutor bell 10000 |> Async.RunSynchronously with
    | Error msg -> eprintfn "Execution failed: %s" msg
    | Ok rawCounts ->
        // Step 3: Apply correction
        match correctReadoutErrors rawCounts calibration remConfig with
        | Ok corrected ->
            printfn "Raw counts: %A" rawCounts
            printfn "Corrected counts: %A" corrected.Histogram
            printfn "95%% intervals: %A" corrected.ConfidenceIntervals
            printfn "Normalization check: %.4f" corrected.GoodnessOfFit
        | Error msg -> eprintfn "Correction failed: %s" msg
```

`correctReadoutErrors` returns `CorrectedResults`: the corrected `Histogram` (non-integer counts), `ConfidenceIntervals`, the `CalibrationUsed` and a `GoodnessOfFit` normalization check. The calibration must match the histogram's qubit count, otherwise the call returns an `Error`.

`ReadoutErrorMitigation.mitigate circuit backendName config executor` runs calibration, execution and correction in one call. It recalibrates every time, so when you run several circuits, calibrate once with `measureCalibrationMatrix` and reuse the matrix with `correctReadoutErrors`.

### Multi-Qubit Calibration

**For n qubits, calibration prepares all 2ⁿ basis states:**

```fsharp
// 1 qubit: 2 states (|0⟩, |1⟩)
let cal1 = measureCalibrationMatrix "noisy-local" 1 remConfig sampleExecutor

// 3 qubits: 8 states (|000⟩, |001⟩, ..., |111⟩)
let cal3 = measureCalibrationMatrix "noisy-local" 3 remConfig sampleExecutor
```

**Calibration Cost**:
- 1 qubit: 2 circuits
- 2 qubits: 4 circuits
- 3 qubits: 8 circuits
- 4 qubits: 16 circuits
- **Scales exponentially** - the library accepts 1-10 qubits and returns an `Error` outside that range

### Handling Negative Counts

**Problem**: Matrix inversion can produce negative counts (unphysical)

**Options** (`REMConfig.ClipNegative`):

```fsharp
// Clip to zero (default): negative values become 0, then the vector is renormalized
let clipping = remConfig |> withClipNegative true

// Keep the quasi-probabilities M^-1 × measured unchanged, negative entries included:
// the unbiased estimate that expectation values need
let unclipped = remConfig |> withClipNegative false
```

Entries below `MinProbability` (default 1% of shots) are dropped from the histogram, negative ones included; lower it with `withMinProbability` if you need small probabilities. Only clipping renormalises. For an unbiased quasi-distribution, as expectation values need, set both `withClipNegative false` and `withMinProbability 0.0`: then nothing is dropped or renormalised.

### Cost Analysis

**Circuit Executions**:
- Calibration: 2ⁿ circuits (one-time)
- Runtime: **no extra executions** (pure post-processing)

**Example Cost** (illustrative prices):
- Calibration (3 qubits): 8 circuits = $8 (one-time)
- Per-circuit cost after calibration: $0

### Working Example

See complete example: [examples/ErrorMitigation/REM_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ErrorMitigation/REM_Example.fsx)

---

## Combined Strategies

### Why Combine Techniques?

Error mitigation techniques target different error sources:
- **ZNE**: Gate errors
- **PEC**: Gate errors (more aggressive)
- **REM**: Readout errors

Combining a gate-error technique with REM addresses both kinds of error. The library has no single "apply everything" function; you combine the techniques by composing executors.

### Recommended Combinations

#### 1. REM + ZNE (Best Value)

**Cost**: Low-Medium (one execution per ZNE noise level)
**Use For**: Most applications on real hardware

The ZNE executor samples the circuit, corrects the histogram with REM, and computes the expectation value from the corrected counts:

```fsharp
/// ⟨Z⊗Z...⟩ from a histogram: +1 for even parity, -1 for odd parity
let parityExpectation (histogram: Map<string, float>) =
    let total = histogram |> Map.fold (fun acc _ count -> acc + count) 0.0

    histogram
    |> Map.fold
        (fun acc bits count ->
            let ones = bits |> Seq.filter ((=) '1') |> Seq.length
            let sign = if ones % 2 = 0 then 1.0 else -1.0
            acc + sign * count / total)
        0.0

/// Executor for ZNE/PEC that applies REM before computing the expectation value
let remCorrectedExecutor (calibration: CalibrationMatrix) (c: Circuit) : Async<Result<float, string>> =
    async {
        let! counts = sampleExecutor c 4000

        return
            counts
            |> Result.bind (fun measured -> correctReadoutErrors measured calibration remConfig)
            |> Result.map (fun corrected -> parityExpectation corrected.Histogram)
    }

// Step 1: Calibrate REM (one-time), then Step 2: run ZNE with the corrected executor
let remZne =
    async {
        match! measureCalibrationMatrix "noisy-local" 2 remConfig sampleExecutor with
        | Error msg -> return Error msg
        | Ok calibration ->
            return! ZeroNoiseExtrapolation.mitigate vqeCircuit zneConfig (remCorrectedExecutor calibration)
    }

match Async.RunSynchronously remZne with
| Ok result -> printfn "Mitigated value: %.4f" result.ZeroNoiseValue
| Error msg -> eprintfn "Error: %s" msg
```

**Benefits**:
- REM is cheap after calibration
- ZNE adds a moderate cost (one execution per noise level)
- Targets both gate and readout errors
- **Recommended default for production**

#### 2. REM + PEC (Maximum Accuracy)

**Cost**: High (`Samples` + 1 executions)
**Use For**: Critical high-accuracy applications

The same corrected executor plugs into PEC:

```fsharp
let remPec =
    async {
        match! measureCalibrationMatrix "noisy-local" 2 remConfig sampleExecutor with
        | Error msg -> return Error msg
        | Ok calibration ->
            return! ProbabilisticErrorCancellation.mitigate vqeCircuit pecConfig (remCorrectedExecutor calibration)
    }

match Async.RunSynchronously remPec with
| Ok result -> printfn "Mitigated value: %.4f" result.CorrectedExpectation
| Error msg -> eprintfn "Error: %s" msg
```

**Benefits**:
- Largest error reduction of the available techniques
- Targets both gate and readout errors
- **Use only when accuracy justifies cost**

### Automatic Strategy Selection

`ErrorMitigationStrategy.selectStrategy` recommends a technique from the circuit size, the target backend, a budget and an accuracy target. The recommendation carries default ZNE/PEC configurations, a fallback, a cost estimate and a human-readable reason.

```fsharp
open FSharp.Azure.Quantum.Core

let criteria : ErrorMitigationStrategy.SelectionCriteria =
    { CircuitDepth = 25
      QubitCount = 2
      Backend = { Id = "ionq.simulator"; Provider = "IonQ"; Name = "IonQ Simulator"; Status = "Available" }
      MaxCostUSD = Some 50.0
      RequiredAccuracy = None
      Calibration = None } // or Some calibration from measureCalibrationMatrix

let recommended = ErrorMitigationStrategy.selectStrategy criteria
printfn "%s (estimated cost %.0fx)" recommended.Reasoning recommended.EstimatedCostMultiplier
```

What `selectStrategy` picks:

| Situation | Primary technique | Fallback |
|-----------|-------------------|----------|
| Budget below $1 | REM | none |
| Fewer than 10 gates | REM | none |
| Required accuracy above 0.9 and budget above $100 | PEC + ZNE + REM | ZNE + REM |
| 10-49 gates and budget above $10 | ZNE + REM | REM |
| 50 or more gates | ZNE + REM | REM |
| Budget below $10 | REM | none |
| Otherwise | ZNE + REM | REM |

`ErrorMitigationStrategy.applyStrategy histogram recommended` applies a recommendation to a finished histogram. Only the readout (REM) part can be applied after the fact, and only when the criteria carried a calibration matrix; otherwise the counts pass through unchanged and the result has `CorrectionApplied = false`. ZNE and PEC re-execute the circuit, so run them with their own `mitigate` functions as shown above.

### Strategy Selection Guide

| Application | Recommended Strategy | Cost |
|-------------|---------------------|------|
| **Prototyping** | REM only | Low |
| **Production** | REM + ZNE | Low-Medium |
| **High-value** | REM + PEC | High |

---

## Performance Comparison

### Error Reduction Effectiveness

Typical ranges from the literature; your results will vary.

| Technique | Gate Errors | Readout Errors | Extra executions | Recommendation |
|-----------|-------------|----------------|------------------|----------------|
| **ZNE** | 30-50% | 0% | one per noise level | Good value |
| **PEC** | 50-80% | 0% | `Samples` + 1 | Critical use only |
| **REM** | 0% | 50-90% | 2ⁿ calibration circuits, once | Almost always |
| **REM+ZNE** | 30-50% | 50-90% | one per noise level | **Best default** |
| **REM+PEC** | 50-80% | 50-90% | `Samples` + 1 | Maximum accuracy |

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
- Add more noise levels (for example five instead of three)
- Use a higher polynomial degree (it needs at least `degree + 1` noise levels)
- Use more shots in your executor to reduce statistical noise
- Check that the noise really grows with circuit length on your backend

#### 2. PEC Overhead Too High

**Symptoms:** Too many samples needed for a stable estimate

**Solutions:**
- Reduce circuit depth (simplify algorithm)
- Use ZNE instead of PEC
- Split computation into shallower sub-circuits
- Consider if accuracy requirement justifies cost

#### 3. REM Produces Negative Counts

**Symptoms:** Unphysical negative counts after correction

**Solutions:**
- Keep `ClipNegative = true` (the default)
- Increase calibration shots (10,000+)
- Check if the confusion matrix is well-conditioned (a nearly singular matrix returns an `Error`)

#### 4. Combined Strategies Don't Improve Accuracy

**Symptoms:** Mitigation makes results worse

**Solutions:**
- Check calibration quality (REM confusion matrix)
- Verify noise model accuracy (for PEC)
- Ensure sufficient samples (ZNE/PEC)
- Check bitstring order in your REM executor (highest qubit first)
- May be dominated by other errors (try different technique)

## Working Examples

See complete, runnable examples in `examples/ErrorMitigation/`:

- **[ZNE_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ErrorMitigation/ZNE_Example.fsx)** - Zero-Noise Extrapolation demo
- **[PEC_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ErrorMitigation/PEC_Example.fsx)** - Probabilistic Error Cancellation demo
- **[REM_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ErrorMitigation/REM_Example.fsx)** - Readout Error Mitigation demo
- **[CombinedStrategy_Example.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ErrorMitigation/CombinedStrategy_Example.fsx)** - Combining multiple techniques
- **[NoisyDensityMatrix.fsx](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/ErrorMitigation/NoisyDensityMatrix.fsx)** - The noisy density-matrix simulator used on this page

## See Also

- [Getting Started Guide](getting-started) - Installation and setup
- [Backend Switching](backend-switching) - Cloud quantum backends
- [Quantum Chemistry example](https://github.com/Thorium/FSharp.Azure.Quantum/tree/main/examples/Chemistry) - VQE (runnable .fsx)
- [API Reference](api-reference) - Complete API documentation

## References

- **Zero-Noise Extrapolation**: [Temme et al., PRL (2017)](https://arxiv.org/abs/1612.02058)
- **Probabilistic Error Cancellation**: [Temme et al., PRL (2017)](https://arxiv.org/abs/1612.02058)
- **Readout Error Mitigation**: [Maciejewski et al., Quantum (2020)](https://arxiv.org/abs/1907.08518)
- **Error Mitigation Review**: [Endo et al., JPSJ (2021)](https://arxiv.org/abs/1808.00709)

---

**Last Updated**: September 2026
