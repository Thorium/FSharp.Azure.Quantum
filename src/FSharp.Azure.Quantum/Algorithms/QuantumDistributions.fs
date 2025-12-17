namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum

/// Quantum-Enhanced Statistical Distributions
/// 
/// Provides quantum random sampling from standard statistical distributions.
/// Uses QRNG as the entropy source, transformed via inverse CDF methods.
/// 
/// **Key Benefit:** True quantum randomness vs pseudo-random classical sampling
/// 
/// **Use Cases:**
/// - Monte Carlo simulations requiring true randomness
/// - Financial modeling (stock prices, option pricing)
/// - Scientific simulations (particle physics, chemistry)
/// - Machine learning (quantum-enhanced training data)
/// 
/// **Method:** Inverse Transform Sampling
/// 1. Generate uniform quantum random U ~ Uniform(0,1) using QRNG
/// 2. Apply inverse CDF: X = F⁻¹(U) where F is target distribution
/// 3. Result: X follows target distribution with quantum entropy
module QuantumDistributions =
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Statistical distribution for quantum sampling
    type Distribution =
        /// Normal (Gaussian) distribution: N(μ, σ²)
        /// Parameters: mean, standard deviation
        | Normal of mean: float * stddev: float
        
        /// Standard normal distribution: N(0, 1)
        | StandardNormal
        
        /// Log-normal distribution: ln(X) ~ N(μ, σ²)
        /// Parameters: mu, sigma (for underlying normal)
        /// Used for stock prices (always positive)
        | LogNormal of mu: float * sigma: float
        
        /// Exponential distribution: f(x) = λe^(-λx)
        /// Parameter: rate (λ)
        /// Used for time between events
        | Exponential of lambda: float
        
        /// Uniform distribution: U(a, b)
        /// Parameters: min, max
        | Uniform of min: float * max: float
        
        /// Custom distribution via transform function
        /// Takes uniform U~(0,1) and applies custom transformation
        | Custom of name: string * transform: (float -> float)
    
    /// Result of quantum distribution sampling
    /// 
    /// **Performance:** Struct type for efficient allocation (stack vs heap)
    [<Struct>]
    type SampleResult = {
        /// Generated sample value
        Value: float
        
        /// Distribution sampled from
        Distribution: Distribution
        
        /// Quantum entropy used (number of quantum bits)
        /// 
        /// - Pure simulation (`sample`): 53 qubits (IEEE 754 double precision)
        /// - Backend-based (`sampleWithBackend`): 10 qubits (configurable)
        QuantumBitsUsed: int
    }
    
    // ========================================================================
    // DISTRIBUTION PARAMETERS VALIDATION
    // ========================================================================
    
    /// Validate distribution parameters
    let private validateDistribution (dist: Distribution) : Result<unit, string> =
        match dist with
        | Normal (mean, stddev) ->
            if stddev <= 0.0 then
                Error "Normal distribution: stddev must be positive"
            elif Double.IsNaN(mean) || Double.IsInfinity(mean) then
                Error "Normal distribution: mean must be finite"
            elif Double.IsNaN(stddev) || Double.IsInfinity(stddev) then
                Error "Normal distribution: stddev must be finite"
            else
                Ok ()
        
        | StandardNormal ->
            Ok ()
        
        | LogNormal (mu, sigma) ->
            if sigma <= 0.0 then
                Error "LogNormal distribution: sigma must be positive"
            elif Double.IsNaN(mu) || Double.IsInfinity(mu) then
                Error "LogNormal distribution: mu must be finite"
            elif Double.IsNaN(sigma) || Double.IsInfinity(sigma) then
                Error "LogNormal distribution: sigma must be finite"
            else
                Ok ()
        
        | Exponential lambda ->
            if lambda <= 0.0 then
                Error "Exponential distribution: lambda must be positive"
            elif Double.IsNaN(lambda) || Double.IsInfinity(lambda) then
                Error "Exponential distribution: lambda must be finite"
            else
                Ok ()
        
        | Uniform (minVal, maxVal) ->
            if minVal >= maxVal then
                Error "Uniform distribution: min must be < max"
            elif Double.IsNaN(minVal) || Double.IsInfinity(minVal) then
                Error "Uniform distribution: min must be finite"
            elif Double.IsNaN(maxVal) || Double.IsInfinity(maxVal) then
                Error "Uniform distribution: max must be finite"
            else
                Ok ()
        
        | Custom (name, _) ->
            if String.IsNullOrWhiteSpace(name) then
                Error "Custom distribution: name cannot be empty"
            else
                Ok ()
    
    // ========================================================================
    // INVERSE CDF TRANSFORMATIONS
    // ========================================================================
    
    /// Clamp probability to avoid numerical edge cases at 0.0 and 1.0
    /// This prevents infinite values from inverse CDFs and log(0) errors
    let private clampProbability (u: float) : float =
        let epsilon = 1e-15
        max epsilon (min (1.0 - epsilon) u)
    
    /// Transform uniform U~(0,1) to target distribution via inverse CDF
    let private transformUniform (dist: Distribution) (u: float) : float =
        // Clamp u to avoid edge cases (0.0 → -∞, 1.0 → +∞)
        let uClamped = clampProbability u
        
        match dist with
        | Normal (mean, stddev) ->
            // Use quantile function from StatisticalDistributions
            // Clamping prevents ±∞ at u=0 or u=1
            let z = StatisticalDistributions.normalQuantile uClamped
            mean + stddev * z
        
        | StandardNormal ->
            // Clamping prevents ±∞ at u=0 or u=1
            StatisticalDistributions.normalQuantile uClamped
        
        | LogNormal (mu, sigma) ->
            // X = exp(μ + σZ) where Z ~ N(0,1)
            // Clamping prevents ±∞ in the exponent
            let z = StatisticalDistributions.normalQuantile uClamped
            exp (mu + sigma * z)
        
        | Exponential lambda ->
            // F⁻¹(u) = -ln(1-u) / λ
            // Clamping prevents log(0) when u=1.0
            -log(1.0 - uClamped) / lambda
        
        | Uniform (minVal, maxVal) ->
            // F⁻¹(u) = min + u(max - min)
            // Clamp result to ensure it stays within [min, max]
            let result = minVal + uClamped * (maxVal - minVal)
            min maxVal (max minVal result)
        
        | Custom (_, transform) ->
            // Apply custom transform with error handling.
            // Note: this function is used by both pure sampling (exception-caught)
            // and backend sampling (mapped to QuantumError.OperationError).
            try
                let result = transform uClamped

                if Double.IsNaN(result) then
                    raise (ArgumentException("Custom transform produced NaN"))
                elif Double.IsInfinity(result) then
                    raise (ArgumentException("Custom transform produced Infinity"))
                else
                    result
            with ex ->
                raise (Exception($"Custom transform failed: {ex.Message}", ex))
    
    // ========================================================================
    // SAMPLING (No Backend - Pure Simulation)
    // ========================================================================
    
    /// Sample from distribution using quantum random bits (pure simulation)
    /// 
    /// **Note:** This uses the standalone QRNG (no backend required).
    /// For backend-based sampling, use `sampleWithBackend`.
    let sample (dist: Distribution) : Result<SampleResult, string> =
        match validateDistribution dist with
        | Error msg -> Error msg
        | Ok () ->
            try
                // Generate quantum uniform random U ~ (0,1)
                // QRNG.generateFloat() uses 53 qubits (IEEE 754 double precision mantissa)
                let numQubits = 53  // Matches QRNG.generateFloat() internal implementation
                let u = QRNG.generateFloat()
                
                // Transform to target distribution
                let value = transformUniform dist u
                
                Ok {
                    Value = value
                    Distribution = dist
                    QuantumBitsUsed = numQubits
                }
            with ex ->
                Error ex.Message
    
    /// Sample multiple values from distribution (pure simulation)
    let sampleMany (dist: Distribution) (count: int) : Result<SampleResult[], string> =
        if count <= 0 then
            Error "Sample count must be positive"
        elif count > 1000000 then
            Error "Sample count too large (max 1,000,000)"
        else
            match validateDistribution dist with
            | Error msg -> Error msg
            | Ok () ->
                [1 .. count]
                |> List.map (fun _ -> sample dist)
                |> List.fold (fun acc res ->
                    match acc, res with
                    | Ok samples, Ok sampleRes -> Ok (sampleRes :: samples)
                    | Error msg, _ -> Error msg
                    | _, Error msg -> Error msg) (Ok [])
                |> Result.map (List.rev >> Array.ofList)
    
    // ========================================================================
    // INTENT → PLAN → EXECUTE (ADR: Intent-First)
    // ========================================================================

    type private SampleIntent = {
        Distribution: Distribution
        NumQubits: int
    }

    [<RequireQualifiedAccess>]
    type private SamplePlan =
        | GenerateUniformViaQrng

    let private plan (backend: IQuantumBackend) (intent: SampleIntent) : Result<SamplePlan, QuantumError> =
        match validateDistribution intent.Distribution with
        | Error msg -> Error (QuantumError.ValidationError ("Distribution", msg))
        | Ok () ->
            if intent.NumQubits <= 0 then
                Error (QuantumError.ValidationError ("NumQubits", "must be positive"))
            elif intent.NumQubits > 20 then
                Error (QuantumError.ValidationError ("NumQubits", "too large for backend execution (max 20)"))
            else
                // QuantumDistributions uses QRNG as its entropy source.
                // If the backend cannot run QRNG, distribution sampling cannot proceed.
                match backend.NativeStateType with
                | QuantumStateType.Annealing ->
                    Error (QuantumError.OperationError ("QuantumDistributions", $"Backend '{backend.Name}' does not support distribution sampling (native state type: {backend.NativeStateType})"))
                | _ ->
                    let needs = QuantumOperation.Gate (CircuitBuilder.H 0)

                    if backend.SupportsOperation needs then
                        Ok SamplePlan.GenerateUniformViaQrng
                    else
                        Error (QuantumError.OperationError ("QuantumDistributions", $"Backend '{backend.Name}' does not support required operations for distribution sampling"))

    let private executePlan
        (backend: IQuantumBackend)
        (intent: SampleIntent)
        (plan: SamplePlan)
        : Async<QuantumResult<SampleResult>> =
        async {
            match plan with
            | SamplePlan.GenerateUniformViaQrng ->
                let! qrngResult = QRNG.generateWithBackend intent.NumQubits backend

                match qrngResult with
                | Error err ->
                    return Error err
                | Ok qrng ->
                    match qrng.AsInteger with
                    | None ->
                        return Error (QuantumError.BackendError ("QuantumDistributions", "Failed to generate uniform random"))
                    | Some bits ->
                        let u = float bits / float (1UL <<< intent.NumQubits)

                        try
                            let value = transformUniform intent.Distribution u

                            return Ok {
                                Value = value
                                Distribution = intent.Distribution
                                QuantumBitsUsed = intent.NumQubits
                            }
                        with ex ->
                            return Error (QuantumError.OperationError ("QuantumDistributions", ex.Message))
        }

    // ========================================================================
    // BACKEND INTEGRATION (RULE1 Compliant)
    // ========================================================================
    
    /// Sample from distribution using quantum backend
    /// 
    /// **RULE1 Compliance:** Requires explicit backend parameter (no default)
    /// 
    /// **Algorithm:**
    /// 1. Generate quantum uniform random U ~ Uniform(0,1) using backend
    /// 2. Transform via inverse CDF: X = F⁻¹(U)
    /// 3. Result follows target distribution with quantum entropy
    /// 
    /// **Quantum Advantage:** True quantum randomness vs pseudo-random
    /// **Note:** Uses 10 qubits (2^10 = 1,024 precision levels) for good balance
    /// between precision and performance. This provides sufficient
    /// precision for most statistical applications while keeping execution fast.
    let sampleWithBackend 
        (dist: Distribution) 
        (backend: IQuantumBackend) 
        : Async<QuantumResult<SampleResult>> =
        
        async {
            let intent = {
                Distribution = dist
                NumQubits = 10
            }

            match plan backend intent with
            | Error err ->
                return Error err
            | Ok chosenPlan ->
                return! executePlan backend intent chosenPlan
        }
    
    /// Sample multiple values from distribution using quantum backend
    /// 
    /// **RULE1 Compliance:** Requires explicit backend parameter (no default)
    /// 
    /// **Performance Note:** Generates samples sequentially. For large counts (>1000),
    /// consider using classical sampling after initial quantum seed generation.
    let sampleManyWithBackend 
        (dist: Distribution) 
        (count: int)
        (backend: IQuantumBackend)
        (progressReporter: Progress.IProgressReporter option)
        : Async<QuantumResult<SampleResult[]>> =
        
        async {
            if count <= 0 then
                return Error (QuantumError.ValidationError ("Count", "must be positive"))
            elif count > 10000 then
                return Error (QuantumError.ValidationError ("Count", "too large for backend execution (max 10,000)"))
            else
                let intent = {
                    Distribution = dist
                    NumQubits = 10
                }

                match plan backend intent with
                | Error err ->
                    return Error err
                | Ok chosenPlan ->
                    // Report start
                    progressReporter
                    |> Option.iter (fun r ->
                        r.Report(Progress.PhaseChanged("Quantum Sampling", Some $"Generating {count} quantum samples...")))

                    // Generate samples sequentially
                    let rec generateSamples (remaining: int) (acc: SampleResult list) =
                        async {
                            if remaining = 0 then
                                return Ok (acc |> List.rev |> Array.ofList)
                            else
                                // Report progress
                                let currentSample = count - remaining + 1
                                progressReporter
                                |> Option.iter (fun r ->
                                    r.Report(Progress.IterationUpdate(currentSample, count, None)))

                                let! sampleResult = executePlan backend intent chosenPlan

                                match sampleResult with
                                | Error execErr ->
                                    return Error execErr
                                | Ok sample ->
                                    return! generateSamples (remaining - 1) (sample :: acc)
                        }

                    let! result = generateSamples count []

                    // Report completion
                    match result with
                    | Ok samples ->
                        progressReporter
                        |> Option.iter (fun r ->
                            r.Report(Progress.PhaseChanged("Sampling Complete", Some $"Generated {samples.Length} quantum samples")))
                    | Error _ -> ()

                    return result
        }
    
    // ========================================================================
    // STATISTICAL UTILITIES
    // ========================================================================
    
    /// Calculate sample statistics (mean, stddev, min, max)
    /// 
    /// **Performance:** Struct type for efficient allocation
    [<Struct>]
    type SampleStatistics = {
        Mean: float
        StdDev: float
        Min: float
        Max: float
        Count: int
    }
    
    /// Compute statistics from sample results
    let computeStatistics (samples: SampleResult[]) : SampleStatistics =
        if samples.Length = 0 then
            failwith "Cannot compute statistics on empty sample array"
        
        let values = samples |> Array.map (fun s -> s.Value)
        let n = float values.Length
        
        let mean = values |> Array.average
        let variance = 
            values 
            |> Array.map (fun x -> (x - mean) ** 2.0)
            |> Array.average
        let stddev = sqrt variance
        
        {
            Mean = mean
            StdDev = stddev
            Min = values |> Array.min
            Max = values |> Array.max
            Count = values.Length
        }
    
    // ========================================================================
    // DISTRIBUTION HELPERS
    // ========================================================================
    
    /// Get distribution name as string
    let distributionName (dist: Distribution) : string =
        match dist with
        | Normal (mean, stddev) -> $"Normal(μ={mean:F2}, σ={stddev:F2})"
        | StandardNormal -> "StandardNormal(μ=0, σ=1)"
        | LogNormal (mu, sigma) -> $"LogNormal(μ={mu:F2}, σ={sigma:F2})"
        | Exponential lambda -> $"Exponential(λ={lambda:F2})"
        | Uniform (minVal, maxVal) -> $"Uniform({minVal:F2}, {maxVal:F2})"
        | Custom (name, _) -> $"Custom({name})"
    
    /// Get expected mean for distribution
    let expectedMean (dist: Distribution) : float option =
        match dist with
        | Normal (mean, _) -> Some mean
        | StandardNormal -> Some 0.0
        | LogNormal (mu, sigma) -> Some (exp (mu + sigma ** 2.0 / 2.0))
        | Exponential lambda -> Some (1.0 / lambda)
        | Uniform (minVal, maxVal) -> Some ((minVal + maxVal) / 2.0)
        | Custom _ -> None  // Unknown for custom distributions
    
    /// Get expected standard deviation for distribution
    let expectedStdDev (dist: Distribution) : float option =
        match dist with
        | Normal (_, stddev) -> Some stddev
        | StandardNormal -> Some 1.0
        | LogNormal (mu, sigma) -> 
            let expMuSigma2 = exp (mu + sigma ** 2.0 / 2.0)
            Some (expMuSigma2 * sqrt (exp (sigma ** 2.0) - 1.0))
        | Exponential lambda -> Some (1.0 / lambda)
        | Uniform (minVal, maxVal) -> 
            Some (sqrt ((maxVal - minVal) ** 2.0 / 12.0))
        | Custom _ -> None  // Unknown for custom distributions
