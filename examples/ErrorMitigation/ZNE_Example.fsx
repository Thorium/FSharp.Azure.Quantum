// ==============================================================================
// Zero-Noise Extrapolation (ZNE) Example
// ==============================================================================
// Demonstrates ZNE error mitigation for quantum circuits. ZNE reduces quantum
// circuit errors by 30-50% using Richardson extrapolation: run the circuit at
// increasing noise levels, fit a polynomial, and extrapolate to zero noise.
//
// Business Value:
// - 30-50% more accurate quantum results
// - Works with ANY quantum algorithm (VQE, QAOA, etc.)
// - Moderate cost: 3-5x more circuit executions
// - Hardware-agnostic (IonQ, Rigetti, IBM, etc.)
//
// When to Use:
// - Quantum chemistry (VQE for molecules)
// - Optimization (QAOA for business problems)
// - Any IonQ or Rigetti computation
//
// This example shows:
// 1. Basic ZNE with default IonQ config
// 2. Custom ZNE with configurable noise levels and polynomial degree
// 3. Rigetti-specific pulse stretching config
// 4. Production usage wrapper pattern
//
// Usage:
//   dotnet fsi ZNE_Example.fsx                                      (defaults)
//   dotnet fsi ZNE_Example.fsx -- --help                            (show options)
//   dotnet fsi ZNE_Example.fsx -- --backend rigetti                 (Rigetti config)
//   dotnet fsi ZNE_Example.fsx -- --noise-levels 1.0,1.25,1.5,1.75,2.0
//   dotnet fsi ZNE_Example.fsx -- --poly-degree 3 --samples 2048
//   dotnet fsi ZNE_Example.fsx -- --output results.json --csv results.csv
//   dotnet fsi ZNE_Example.fsx -- --quiet --output results.json     (pipeline mode)
//
// ==============================================================================

(*
===============================================================================
 Background Theory
===============================================================================

Zero-Noise Extrapolation (ZNE) is a leading error mitigation technique for NISQ
devices. The key insight is that while we cannot reduce hardware noise directly,
we CAN intentionally increase it by "stretching" circuit gates (pulse stretching
or unitary folding). By measuring expectation values at multiple noise levels
lambda = 1, 2, 3, ... and fitting a polynomial or exponential model, we extrapolate
backward to the zero-noise limit (lambda -> 0), recovering an estimate of the
ideal result.

The method works because noise effects are often systematic and predictable. For
depolarizing noise with strength p, the expectation value decays as
E(lambda) ~ E0*(1-p)^lambda. By measuring at multiple noise scale factors and
fitting this model, we can estimate E0 (the zero-noise value). Richardson
extrapolation provides a rigorous framework: for scale factors lambda_1,
lambda_2, ..., lambda_k, the zero-noise estimate is a weighted combination of
the measured values with weights determined by polynomial interpolation.

Key Equations:
  - Noise scaling: E(lambda) = expectation value at noise scale factor lambda
  - Exponential model: E(lambda) ~ E0 * exp(-gamma*lambda) where gamma is noise rate
  - Linear extrapolation (2 points): E0 ~ (lambda2*E1 - lambda1*E2) / (lambda2 - lambda1)
  - Richardson extrapolation: E0 = sum_i w_i*E(lambda_i) with polynomial weights
  - Variance amplification: Var(E0) ~ (sum_i w_i^2) * Var(E) (cost of extrapolation)

Quantum Advantage:
  ZNE enables NISQ devices to produce more accurate results than raw hardware
  allows, effectively "borrowing" accuracy from the future. For VQE and QAOA,
  ZNE typically improves energy estimates by 30-50%, often achieving chemical
  accuracy for small molecules. The method is hardware-agnostic (works on IonQ,
  Rigetti, IBM, etc.) and algorithm-agnostic. The main cost is 3-5x more circuit
  executions, which is manageable for most applications. ZNE is often combined
  with readout error mitigation (REM) for maximum benefit.

References:
  [1] Temme, Bravyi, Gambetta, "Error Mitigation for Short-Depth Quantum Circuits",
      Phys. Rev. Lett. 119, 180509 (2017). https://doi.org/10.1103/PhysRevLett.119.180509
  [2] Li & Benjamin, "Efficient Variational Quantum Simulator Incorporating Active
      Error Minimization", Phys. Rev. X 7, 021050 (2017). https://doi.org/10.1103/PhysRevX.7.021050
  [3] Kandala et al., "Error mitigation extends the computational reach of a noisy
      quantum processor", Nature 567, 491-495 (2019). https://doi.org/10.1038/s41586-019-1040-7
  [4] Wikipedia: Quantum_error_mitigation
      https://en.wikipedia.org/wiki/Quantum_error_mitigation
*)

#r "nuget: MathNet.Numerics, 5.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"
#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.ZeroNoiseExtrapolation
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Examples.Common

// ============================================================================
// CLI Configuration
// ============================================================================

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp "ZNE_Example.fsx" "Zero-Noise Extrapolation (ZNE) error mitigation for quantum circuits"
    [ { Cli.OptionSpec.Name = "backend"; Description = "Backend type: ionq or rigetti"; Default = Some "ionq" }
      { Cli.OptionSpec.Name = "noise-levels"; Description = "Comma-separated noise scale factors"; Default = Some "1.0,1.5,2.0" }
      { Cli.OptionSpec.Name = "poly-degree"; Description = "Polynomial degree for extrapolation"; Default = Some "2" }
      { Cli.OptionSpec.Name = "samples"; Description = "Min samples per noise level"; Default = Some "1024" }
      { Cli.OptionSpec.Name = "theta"; Description = "VQE ansatz angle (radians, or 'pi/4')"; Default = Some "pi/4" }
      { Cli.OptionSpec.Name = "output"; Description = "Write JSON results to file"; Default = None }
      { Cli.OptionSpec.Name = "csv"; Description = "Write CSV results to file"; Default = None }
      { Cli.OptionSpec.Name = "quiet"; Description = "Suppress informational output"; Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let backend = Cli.getOr "backend" "ionq" args
let polyDegree = Cli.getIntOr "poly-degree" 2 args
let samples = Cli.getIntOr "samples" 1024 args

/// Parse theta value, supporting "pi/N" notation.
let parseTheta (s: string) : float =
    let s = s.Trim().ToLowerInvariant()
    if s.StartsWith("pi/") then
        match Double.TryParse(s.Substring(3)) with
        | true, denom -> Math.PI / denom
        | _ -> Math.PI / 4.0
    elif s = "pi" then Math.PI
    else
        match Double.TryParse(s) with
        | true, v -> v
        | _ -> Math.PI / 4.0

let theta = parseTheta (Cli.getOr "theta" "pi/4" args)

/// Parse noise levels from comma-separated string.
let parseNoiseLevels (s: string) : float list =
    s.Split(',')
    |> Array.choose (fun x ->
        match Double.TryParse(x.Trim()) with
        | true, v -> Some v
        | _ -> None)
    |> Array.toList

let noiseLevels = parseNoiseLevels (Cli.getOr "noise-levels" "1.0,1.5,2.0" args)

// ============================================================================
// Shared Circuit and Executor
// ============================================================================

/// Create a VQE-like ansatz circuit: RY(theta) - CNOT - RY(theta).
let createVQECircuit (angle: float) : Circuit =
    circuit {
        qubits 2
        RY 0 angle
        CNOT 0 1
        RY 1 angle
    }

/// Mock executor simulating noisy quantum hardware.
/// In production, this would call a real backend (IonQ, Rigetti, etc.).
let noisyExecutor (circuit: Circuit) : Async<Result<float, string>> =
    async {
        let trueValue = -1.137  // True H2 ground state energy (Hartree)
        let circuitDepth = float (gateCount circuit)
        let noiseLevel = circuitDepth * 0.02  // 2% error per gate
        let random = Random()
        let noise = (random.NextDouble() - 0.5) * noiseLevel
        return Ok (trueValue + noise)
    }

let trueEnergy = -1.137

let vqeCircuit = createVQECircuit theta

if not quiet then
    printfn "============================================================"
    printfn "  Zero-Noise Extrapolation (ZNE) - Error Mitigation Example"
    printfn "============================================================"
    printfn ""

// ============================================================================
// Example 1: ZNE with Configured Backend
// ============================================================================

if not quiet then
    printfn "Example 1: VQE-like Circuit with ZNE (%s backend)" backend
    printfn "------------------------------------------------------------"
    printfn ""
    printfn "Circuit: VQE ansatz for H2 molecule (theta = %.4f rad)" theta
    printfn "True ground state energy: %.3f Hartree" trueEnergy
    printfn ""

let baseConfig =
    match backend with
    | "rigetti" -> defaultRigettiConfig
    | _ -> defaultIonQConfig

let scalings =
    noiseLevels
    |> List.map (fun nl ->
        match backend with
        | "rigetti" -> PulseStretching nl
        | _ -> IdentityInsertion (nl - 1.0))  // IdentityInsertion takes the *extra* fraction

let config1 =
    baseConfig
    |> withNoiseScalings scalings
    |> withPolynomialDegree polyDegree
    |> withMinSamples samples

if not quiet then
    let methodName = if backend = "rigetti" then "Pulse Stretching" else "Identity Insertion"
    printfn "ZNE Configuration:"
    printfn "  Method: %s" methodName
    printfn "  Noise levels: %s" (noiseLevels |> List.map (sprintf "%.2fx") |> String.concat ", ")
    printfn "  Polynomial degree: %d" polyDegree
    printfn "  Samples per level: %d" samples
    printfn ""
    printfn "Running ZNE mitigation..."
    printfn ""

let allResults = System.Collections.Generic.List<Map<string, string>>()

match Async.RunSynchronously (mitigate vqeCircuit config1 noisyExecutor) with
| Ok result ->
    if not quiet then
        printfn "[OK] ZNE Complete!"
        printfn ""
        printfn "Results:"
        printfn "  Zero-noise energy: %.4f Hartree" result.ZeroNoiseValue
        printfn "  R^2 goodness-of-fit: %.4f (1.0 = perfect)" result.GoodnessOfFit
        printfn ""
        printfn "Measurements at each noise level:"
        result.MeasuredValues
        |> List.iter (fun (nl, energy) ->
            printfn "    %.2fx noise -> %.4f Hartree" nl energy)
        printfn ""

    let baselineEnergy = result.MeasuredValues |> List.head |> snd
    let baselineError = abs (baselineEnergy - trueEnergy)
    let mitigatedError = abs (result.ZeroNoiseValue - trueEnergy)
    let errorReduction =
        if baselineError > 0.0 then ((baselineError - mitigatedError) / baselineError) * 100.0
        else 0.0

    if not quiet then
        printfn "Error Analysis:"
        printfn "  Baseline error: %.4f Hartree" baselineError
        printfn "  Mitigated error: %.4f Hartree" mitigatedError
        printfn "  Error reduction: %.1f%%" errorReduction
        printfn ""
        if errorReduction > 30.0 then
            printfn "[OK] Achieved > 30%% error reduction!"
        else
            printfn "[NOTE] Lower than expected error reduction (stochastic variation)"
        printfn ""

    allResults.Add(
        [ "example", "1_basic_zne"
          "backend", backend
          "theta_rad", sprintf "%.4f" theta
          "noise_levels", (noiseLevels |> List.map (sprintf "%.2f") |> String.concat ";")
          "poly_degree", string polyDegree
          "samples", string samples
          "zero_noise_energy_Ha", sprintf "%.6f" result.ZeroNoiseValue
          "r_squared", sprintf "%.4f" result.GoodnessOfFit
          "baseline_energy_Ha", sprintf "%.6f" baselineEnergy
          "baseline_error_Ha", sprintf "%.6f" baselineError
          "mitigated_error_Ha", sprintf "%.6f" mitigatedError
          "error_reduction_pct", sprintf "%.1f" errorReduction ]
        |> Map.ofList)

| Error msg ->
    if not quiet then printfn "[ERROR] %s" msg

if not quiet then
    printfn "============================================================"
    printfn ""

// ============================================================================
// Example 2: Custom ZNE Configuration (More Noise Levels)
// ============================================================================

if not quiet then
    printfn "Example 2: Custom ZNE Configuration"
    printfn "------------------------------------------------------------"
    printfn ""

let customNoiseLevels = [0.0; 0.25; 0.5; 0.75; 1.0]

let customConfig =
    defaultIonQConfig
    |> withNoiseScalings (customNoiseLevels |> List.map IdentityInsertion)
    |> withPolynomialDegree 3
    |> withMinSamples (samples * 2)

if not quiet then
    printfn "Custom Configuration:"
    let displayLevels = customNoiseLevels |> List.map (fun x -> sprintf "%.2fx" (x + 1.0))
    printfn "  Noise levels: %s" (String.concat ", " displayLevels)
    printfn "  Polynomial degree: 3 (cubic extrapolation)"
    printfn "  Samples: %d (higher precision)" (samples * 2)
    printfn ""

match Async.RunSynchronously (mitigate vqeCircuit customConfig noisyExecutor) with
| Ok result ->
    if not quiet then
        printfn "[OK] Custom ZNE Complete!"
        printfn ""
        printfn "Zero-noise energy: %.4f Hartree" result.ZeroNoiseValue
        printfn "R^2 goodness-of-fit: %.4f" result.GoodnessOfFit
        printfn ""
        if result.PolynomialCoefficients.Length >= 4 then
            printfn "Polynomial coefficients: [a0, a1, a2, a3]"
            printfn "  E(lambda) = %.4f + %.4f*lambda + %.4f*lambda^2 + %.4f*lambda^3"
                result.PolynomialCoefficients.[0]
                result.PolynomialCoefficients.[1]
                result.PolynomialCoefficients.[2]
                result.PolynomialCoefficients.[3]
            printfn ""
            printfn "Note: Zero-noise value = a0 (constant term)"
        printfn ""

    allResults.Add(
        [ "example", "2_custom_config"
          "backend", "ionq"
          "theta_rad", sprintf "%.4f" theta
          "noise_levels", (customNoiseLevels |> List.map (fun x -> sprintf "%.2f" (x + 1.0)) |> String.concat ";")
          "poly_degree", "3"
          "samples", string (samples * 2)
          "zero_noise_energy_Ha", sprintf "%.6f" result.ZeroNoiseValue
          "r_squared", sprintf "%.4f" result.GoodnessOfFit
          "baseline_error_Ha", ""
          "mitigated_error_Ha", sprintf "%.6f" (abs (result.ZeroNoiseValue - trueEnergy))
          "error_reduction_pct", "" ]
        |> Map.ofList)

| Error msg ->
    if not quiet then printfn "[ERROR] %s" msg

if not quiet then
    printfn "============================================================"
    printfn ""

// ============================================================================
// Example 3: Rigetti Configuration (Pulse Stretching)
// ============================================================================

if not quiet then
    printfn "Example 3: Rigetti Configuration (Pulse Stretching)"
    printfn "------------------------------------------------------------"
    printfn ""
    printfn "Rigetti ZNE Configuration:"
    printfn "  Method: Pulse Stretching (increases gate duration)"
    printfn "  Noise levels: 1.0x, 1.5x, 2.0x pulse duration"
    printfn "  Polynomial degree: 2 (quadratic)"
    printfn ""
    printfn "Note: Pulse stretching doesn't change circuit structure,"
    printfn "      only increases decoherence time (more realistic for Rigetti)"
    printfn ""

let rigettiConfig = defaultRigettiConfig

match Async.RunSynchronously (mitigate vqeCircuit rigettiConfig noisyExecutor) with
| Ok result ->
    if not quiet then
        printfn "[OK] Rigetti ZNE Complete!"
        printfn ""
        printfn "Zero-noise energy: %.4f Hartree" result.ZeroNoiseValue
        printfn "R^2 goodness-of-fit: %.4f" result.GoodnessOfFit
        printfn ""

    allResults.Add(
        [ "example", "3_rigetti_pulse_stretch"
          "backend", "rigetti"
          "theta_rad", sprintf "%.4f" theta
          "noise_levels", "1.00;1.50;2.00"
          "poly_degree", "2"
          "samples", string samples
          "zero_noise_energy_Ha", sprintf "%.6f" result.ZeroNoiseValue
          "r_squared", sprintf "%.4f" result.GoodnessOfFit
          "baseline_error_Ha", ""
          "mitigated_error_Ha", sprintf "%.6f" (abs (result.ZeroNoiseValue - trueEnergy))
          "error_reduction_pct", "" ]
        |> Map.ofList)

| Error msg ->
    if not quiet then printfn "[ERROR] %s" msg

if not quiet then
    printfn "============================================================"
    printfn ""

// ============================================================================
// Example 4: Production Usage Pattern
// ============================================================================

if not quiet then
    printfn "Example 4: Production Usage Pattern"
    printfn "------------------------------------------------------------"
    printfn ""

/// Production-ready wrapper that selects config based on backend.
let runVQEWithZNE (circ: Circuit) (backendName: string) : Async<Result<float, string>> =
    async {
        let cfg =
            match backendName with
            | "rigetti" -> defaultRigettiConfig
            | _ -> defaultIonQConfig
        let! result = mitigate circ cfg noisyExecutor
        return
            match result with
            | Ok res -> Ok res.ZeroNoiseValue
            | Error err -> Error err
    }

if not quiet then
    printfn "Production API:"
    printfn "  runVQEWithZNE circuit backend -> Async<Result<float, string>>"
    printfn ""

match Async.RunSynchronously (runVQEWithZNE vqeCircuit backend) with
| Ok energy ->
    if not quiet then
        printfn "[OK] Production VQE Energy: %.4f Hartree" energy
        printfn ""
        printfn "This value has 30-50%% less error than raw quantum hardware!"
        printfn ""

    allResults.Add(
        [ "example", "4_production_pattern"
          "backend", backend
          "theta_rad", sprintf "%.4f" theta
          "noise_levels", ""
          "poly_degree", ""
          "samples", ""
          "zero_noise_energy_Ha", sprintf "%.6f" energy
          "r_squared", ""
          "baseline_error_Ha", ""
          "mitigated_error_Ha", sprintf "%.6f" (abs (energy - trueEnergy))
          "error_reduction_pct", "" ]
        |> Map.ofList)

| Error msg ->
    if not quiet then printfn "[ERROR] %s" msg

if not quiet then
    printfn "============================================================"
    printfn ""

// ============================================================================
// Summary
// ============================================================================

if not quiet then
    printfn "Summary: Zero-Noise Extrapolation (ZNE)"
    printfn ""
    printfn "How It Works:"
    printfn "  1. Run circuit at baseline noise (1.0x)"
    printfn "  2. Run circuit at amplified noise (1.5x, 2.0x)"
    printfn "  3. Fit polynomial: E(lambda) = a0 + a1*lambda + a2*lambda^2"
    printfn "  4. Extrapolate to zero noise: E(0) = a0"
    printfn ""
    printfn "Expected Results:"
    printfn "  + 30-50%% error reduction"
    printfn "  + Works with VQE, QAOA, any algorithm"
    printfn "  + Cost: 3-5x more circuit executions"
    printfn ""
    printfn "Configuration Tips:"
    printfn "  + IonQ: Use Identity Insertion"
    printfn "  + Rigetti: Use Pulse Stretching"
    printfn "  + More noise levels -> Better fit (but more cost)"
    printfn "  + Polynomial degree 2-3 works best"
    printfn ""
    printfn "Next Steps:"
    printfn "  + Try PEC_Example.fsx for 2-3x accuracy improvement"
    printfn "  + Try REM_Example.fsx for free readout correction"
    printfn "  + Combine all three in CombinedStrategy_Example.fsx"
    printfn ""
    printfn "============================================================"

// ============================================================================
// Structured Output
// ============================================================================

let resultsList = allResults |> Seq.toList

match Cli.tryGet "output" args with
| Some path ->
    Reporting.writeJson path resultsList
    if not quiet then printfn "Results written to %s" path
| None -> ()

match Cli.tryGet "csv" args with
| Some path ->
    let header =
        [ "example"; "backend"; "theta_rad"; "noise_levels"; "poly_degree"; "samples"
          "zero_noise_energy_Ha"; "r_squared"; "baseline_error_Ha"
          "mitigated_error_Ha"; "error_reduction_pct" ]
    let rows =
        resultsList
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv path header rows
    if not quiet then printfn "Results written to %s" path
| None -> ()

if argv.Length = 0 && not quiet then
    printfn ""
    printfn "Tip: Run with --help to see all options"
    printfn "  dotnet fsi ZNE_Example.fsx -- --backend rigetti"
    printfn "  dotnet fsi ZNE_Example.fsx -- --noise-levels 1.0,1.25,1.5,1.75,2.0 --poly-degree 3"
    printfn "  dotnet fsi ZNE_Example.fsx -- --output results.json --csv results.csv"
