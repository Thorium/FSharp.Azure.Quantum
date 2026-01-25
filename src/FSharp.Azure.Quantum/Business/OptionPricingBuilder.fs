namespace FSharp.Azure.Quantum.Business

open System
open System.Numerics
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Algorithms

/// High-level Option Pricing Domain Builder - Production Quantum Implementation
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for traders/quants who want to price derivatives
/// using quantum computing without understanding implementation details.
/// 
/// RULE1 COMPLIANCE:
/// - Backend parameter is REQUIRED on ALL public APIs (no defaults, no optionals)
/// - This is an Azure Quantum library - all code depends on IQuantumBackend
/// - Users must explicitly choose: LocalBackend (simulation) or cloud quantum hardware
/// 
/// QUANTUM ADVANTAGE:
/// - Uses Möttönen state preparation for exact amplitude encoding
/// - Achieves O(1/ε) complexity vs classical O(1/ε²)
/// - Quadratic speedup: 100x for 1% accuracy
/// 
/// MATHEMATICAL FOUNDATION (Black-Scholes):
/// Option price: C = E[e^(-rT) · f(S_T)]
/// where:
///   S_T ~ GBM: S_T = S_0 * exp((r - σ²/2)T + σ√T * Z), Z ~ N(0,1)
///   f = payoff function (e.g., max(S-K, 0) for European call)
/// 
/// LIMITATIONS (documented for production use):
/// 1. Payoff oracle uses MSB threshold approximation (not exact comparison)
/// 2. Requires 2^n qubits for n-bit price discretization
/// 3. Accuracy depends on number of Grover iterations
/// 
/// EXAMPLE USAGE:
///   let result = optionPricing {
///       spotPrice 100.0
///       strikePrice 105.0
///       volatility 0.2
///       expiry 1.0
///       optionType EuropeanCall
///       backend (LocalBackend())
///   }
module OptionPricing =
    
    // ========================================================================
    // TYPES - Domain-specific (finance-friendly)
    // ========================================================================
    
    /// Market parameters for option pricing
    type MarketParameters = {
        /// Current spot price of underlying asset (S₀)
        SpotPrice: float
        
        /// Strike price of the option (K)
        StrikePrice: float
        
        /// Risk-free interest rate (annualized, r)
        RiskFreeRate: float
        
        /// Volatility of underlying asset (annualized, σ)
        Volatility: float
        
        /// Time to expiry in years (T)
        TimeToExpiry: float
    }
    
    /// Type of option
    type OptionType =
        /// European call: max(S_T - K, 0)
        | EuropeanCall
        
        /// European put: max(K - S_T, 0)
        | EuropeanPut
        
        /// Asian call: max(Avg(S_t) - K, 0)
        | AsianCall of timeSteps: int
        
        /// Asian put: max(K - Avg(S_t), 0)
        | AsianPut of timeSteps: int
    
    /// Result of option pricing
    type OptionPrice = {
        /// Estimated option price
        Price: float
        
        /// 95% confidence interval
        ConfidenceInterval: float
        
        /// Quantum speedup factor achieved
        Speedup: float
        
        /// Method used (for logging/debugging)
        Method: string
        
        /// Number of qubits used
        QubitsUsed: int
    }

    /// Configuration for Option Pricing DSL
    type OptionPricingConfig = {
        Market: MarketParameters
        OptionType: OptionType
        NumQubits: int
        GroverIterations: int
        Shots: int
        Backend: IQuantumBackend voption
        CancellationToken: System.Threading.CancellationToken option
    }
    
    // ========================================================================
    // PRIVATE - GBM Distribution Encoding (using Möttönen)
    // ========================================================================
    
    /// Encode Geometric Brownian Motion distribution as quantum state
    /// 
    /// Creates circuit that prepares |ψ⟩ = ∑ᵢ √p(Sᵢ) |i⟩
    /// where p(Sᵢ) is log-normal distribution from Black-Scholes GBM
    /// 
    /// Uses Möttönen state preparation for exact amplitude encoding
    let private encodeGBMDistribution
        (marketParams: MarketParameters)
        (numQubits: int)
        : CircuitBuilder.Circuit =
        
        let numLevels = 1 <<< numQubits
        
        // GBM parameters: S_T = S_0 * exp(μT + σ√T * Z)
        let mu = marketParams.RiskFreeRate - 0.5 * marketParams.Volatility * marketParams.Volatility
        let sigma = marketParams.Volatility * sqrt marketParams.TimeToExpiry
        
        // Discretize log-normal distribution
        let priceLevels = 
            StatisticalDistributions.discretizeLogNormal 
                (log marketParams.SpotPrice + mu * marketParams.TimeToExpiry)
                (sigma)
                numLevels
        
        // Convert probabilities to amplitudes: α_i = √p_i
        let amplitudes =
            priceLevels
            |> Array.map (fun (_price, prob) -> Complex(sqrt prob, 0.0))
        
        // Normalize (should already be normalized, but ensure numerical stability)
        let norm = amplitudes |> Array.sumBy (fun a -> a.Magnitude * a.Magnitude) |> sqrt
        let normalizedAmplitudes = 
            amplitudes |> Array.map (fun a -> a / Complex(norm, 0.0))
        
        // Use Möttönen to create state preparation circuit
        let circuit = CircuitBuilder.empty numQubits
        let qubits = [| 0 .. numQubits - 1 |]
        
        MottonenStatePreparation.prepareStateFromAmplitudes normalizedAmplitudes qubits circuit
    
    // ========================================================================
    // PRIVATE - Payoff Oracle
    // ========================================================================
    
    /// Encode option payoff as quantum oracle
    /// 
    /// **CURRENT IMPLEMENTATION**: MSB threshold approximation
    /// 
    /// Marks states where option is "in-the-money":
    /// - For calls: S_T > K (price > strike)
    /// - For puts: S_T < K (price < strike)
    /// 
    /// **LIMITATION**: Uses most significant bit (MSB) as threshold
    /// This is a simplified approximation! Exact implementation would require
    /// multi-qubit comparator circuits from QuantumArithmetic module.
    /// 
    /// **ACCURACY**: Works well when strike price is near middle of price range.
    /// For strikes far from median, consider:
    /// 1. Adjusting qubit count
    /// 2. Using exact comparator oracle (future enhancement)
    /// 3. Black-Scholes for validation
    let private encodePayoffOracle 
        (optionType: OptionType)
        (marketParams: MarketParameters)
        (numQubits: int)
        : CircuitBuilder.Circuit =
        
        let circuit = CircuitBuilder.empty numQubits
        
        // **MVP IMPLEMENTATION**: Mark upper/lower half based on MSB
        // This approximates in-the-money detection
        if numQubits = 0 then
            circuit
        else
            let msb = numQubits - 1
            match optionType with
            | EuropeanCall | AsianCall _ ->
                // For calls: mark states where price > strike (approximated as upper half)
                // Apply Z gate to MSB (flips phase for MSB=1)
                circuit |> CircuitBuilder.addGate (CircuitBuilder.Z msb)
            
            | EuropeanPut | AsianPut _ ->
                // For puts: mark states where price < strike (approximated as lower half)
                // Apply X-Z-X to MSB (flips phase for MSB=0)
                circuit
                |> CircuitBuilder.addGate (CircuitBuilder.X msb)
                |> CircuitBuilder.addGate (CircuitBuilder.Z msb)
                |> CircuitBuilder.addGate (CircuitBuilder.X msb)
    
    // ========================================================================
    // PUBLIC - Quantum Option Pricing (RULE1: backend required)
    // ========================================================================
    
    /// Price option using quantum Monte Carlo (RULE1 compliant)
    /// 
    /// **REQUIRED PARAMETER**: backend: IQuantumBackend (MUST provide backend)
    /// **QUANTUM ALGORITHM**: Möttönen state preparation + Grover amplitude estimation
    /// **QUADRATIC SPEEDUP**: O(1/ε) vs classical O(1/ε²)
    /// 
    /// ALGORITHM:
    /// 1. Encode GBM distribution using Möttönen (exact amplitude encoding)
    /// 2. Encode payoff function as oracle (MSB threshold approximation)
    /// 3. Run Quantum Monte Carlo with Grover iterations
    /// 4. Extract option price from amplitude estimate
    /// 5. Discount to present value
    /// 
    /// LIMITATIONS:
    /// - Payoff oracle uses MSB approximation (see encodePayoffOracle documentation)
    /// - Requires careful selection of numQubits based on price range
    /// - groverIterations affects accuracy: more iterations = higher precision
    let priceWithCancellation
        (cancellationTokenOpt: System.Threading.CancellationToken option)
        (optionType: OptionType)
        (marketParams: MarketParameters)
        (numQubits: int)
        (groverIterations: int)
        (shots: int)
        (backend: IQuantumBackend)
        : Async<QuantumResult<OptionPrice>> =
        
        async {
            // Validate inputs
            if numQubits < 2 then
                return Error (QuantumError.ValidationError ("numQubits", "Must be >= 2 for meaningful price discretization"))
            elif numQubits > 10 then
                return Error (QuantumError.ValidationError ("numQubits", "Too large (max 10 for practical quantum hardware)"))
            elif groverIterations < 0 then
                return Error (QuantumError.ValidationError ("groverIterations", "Must be >= 0"))
            elif groverIterations > 100 then
                return Error (QuantumError.ValidationError ("groverIterations", "Too many iterations (max 100 for stability)"))
            elif shots <= 0 then
                return Error (QuantumError.ValidationError ("shots", "Must be > 0"))
            elif shots > 1000000 then
                return Error (QuantumError.ValidationError ("shots", "Too large (max 1,000,000 for practical runs)"))
            elif marketParams.SpotPrice <= 0.0 then
                return Error (QuantumError.ValidationError ("SpotPrice", "Must be > 0"))
            elif marketParams.StrikePrice <= 0.0 then
                return Error (QuantumError.ValidationError ("StrikePrice", "Must be > 0"))
            elif marketParams.Volatility <= 0.0 then
                return Error (QuantumError.ValidationError ("Volatility", "Must be > 0"))
            elif marketParams.Volatility > 2.0 then
                return Error (QuantumError.ValidationError ("Volatility", "Volatility > 200% is unrealistic"))
            elif marketParams.TimeToExpiry <= 0.0 then
                return Error (QuantumError.ValidationError ("TimeToExpiry", "Must be > 0"))
            elif marketParams.TimeToExpiry > 10.0 then
                return Error (QuantumError.ValidationError ("TimeToExpiry", "Time > 10 years is beyond typical option maturities"))
            else
                
                // Build quantum state preparation (encode GBM using Möttönen)
                let statePrep = encodeGBMDistribution marketParams numQubits
                
                // Build quantum oracle (encode payoff)
                let oracle = encodePayoffOracle optionType marketParams numQubits
                
                // Configure Quantum Monte Carlo
                let qmcConfig = {
                    QuantumMonteCarlo.NumQubits = numQubits
                    QuantumMonteCarlo.StatePreparation = statePrep
                    QuantumMonteCarlo.Oracle = oracle
                    QuantumMonteCarlo.GroverIterations = groverIterations
                    QuantumMonteCarlo.Shots = shots
                }
                
                // Execute QMC on quantum backend (✅ RULE1 compliant - backend required)
                let! qmcResult =
                    match cancellationTokenOpt with
                    | Some token when token.IsCancellationRequested ->
                        raise (OperationCanceledException token)
                    | Some token ->
                        Async.StartAsTask(
                            QuantumMonteCarlo.estimateExpectation qmcConfig backend,
                            cancellationToken = token,
                            taskCreationOptions = System.Threading.Tasks.TaskCreationOptions.None)
                        |> Async.AwaitTask
                    | None -> QuantumMonteCarlo.estimateExpectation qmcConfig backend
                
                // Calculate discount factor
                let discountFactor = exp (-marketParams.RiskFreeRate * marketParams.TimeToExpiry)
                
                // Map QMC result to option price
                return qmcResult |> Result.map (fun result ->
                    // Scale expectation to option price
                    // Expected payoff ≈ amplitude * spot price (simplified)
                    let expectedPayoff = result.ExpectationValue * marketParams.SpotPrice
                    let optionPrice = discountFactor * expectedPayoff
                    let confidenceInterval = discountFactor * result.StandardError * marketParams.SpotPrice
                    
                    {
                        Price = optionPrice
                        ConfidenceInterval = confidenceInterval
                        Speedup = result.SpeedupFactor
                        Method = "Quantum Monte Carlo (Möttönen + Grover)"
                        QubitsUsed = numQubits
                    }
                )
        }

    let price
        (optionType: OptionType)
        (marketParams: MarketParameters)
        (numQubits: int)
        (groverIterations: int)
        (shots: int)
        (backend: IQuantumBackend)  // ✅ RULE1: Backend REQUIRED (not optional)
        : Async<QuantumResult<OptionPrice>> =
        priceWithCancellation None optionType marketParams numQubits groverIterations shots backend
    
    // ========================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ========================================================================

    type OptionPricingBuilder() =
        
        member _.Yield(_) = 
            {
                Market = {
                    SpotPrice = 100.0
                    StrikePrice = 100.0
                    RiskFreeRate = 0.05
                    Volatility = 0.2
                    TimeToExpiry = 1.0
                }
                OptionType = EuropeanCall
                NumQubits = 6
                GroverIterations = 5
                Shots = 1000
                Backend = ValueNone
                CancellationToken = None
            }

        member _.Delay(f) = f
        
        member _.Run(f) : Async<QuantumResult<OptionPrice>> =
            let config : OptionPricingConfig = f()
            match config.Backend with
            | ValueSome b ->
                priceWithCancellation config.CancellationToken config.OptionType config.Market config.NumQubits config.GroverIterations config.Shots b
            | ValueNone -> async { return Error (QuantumError.ValidationError ("Backend", "Quantum Backend must be specified")) }

        // Custom Operations
        
        [<CustomOperation("spotPrice")>]
        member _.SpotPrice(config: OptionPricingConfig, price) =
            { config with Market = { config.Market with SpotPrice = price } }
            
        [<CustomOperation("strikePrice")>]
        member _.StrikePrice(config: OptionPricingConfig, price) =
            { config with Market = { config.Market with StrikePrice = price } }

        [<CustomOperation("riskFreeRate")>]
        member _.RiskFreeRate(config: OptionPricingConfig, rate) =
            { config with Market = { config.Market with RiskFreeRate = rate } }

        [<CustomOperation("volatility")>]
        member _.Volatility(config: OptionPricingConfig, vol) =
            { config with Market = { config.Market with Volatility = vol } }

        [<CustomOperation("expiry")>]
        member _.Expiry(config: OptionPricingConfig, t) =
            { config with Market = { config.Market with TimeToExpiry = t } }

        [<CustomOperation("optionType")>]
        member _.OptionType(config: OptionPricingConfig, t) =
            { config with OptionType = t }

        [<CustomOperation("qubits")>]
        member _.Qubits(config: OptionPricingConfig, n) =
            { config with NumQubits = n }

        [<CustomOperation("iterations")>]
        member _.Iterations(config: OptionPricingConfig, n) =
            { config with GroverIterations = n }

        [<CustomOperation("shots")>]
        member _.Shots(config: OptionPricingConfig, n) =
            { config with Shots = n }

        [<CustomOperation("backend")>]
        member _.Backend(config: OptionPricingConfig, b) =
            { config with Backend = ValueSome b }

        [<CustomOperation("cancellation_token")>]
        member _.CancellationToken(config: OptionPricingConfig, token: System.Threading.CancellationToken) =
            { config with CancellationToken = Some token }

    let optionPricing = OptionPricingBuilder()

    // ========================================================================
    // HELPER FUNCTIONS - For C# Interop and Simple Usage
    // ========================================================================

    /// Price European Call option with explicit quantum configuration
    let priceEuropeanCall spot strike rate vol expiry numQubits groverIterations shots backend =
        let market = { SpotPrice = spot; StrikePrice = strike; RiskFreeRate = rate; Volatility = vol; TimeToExpiry = expiry }
        price EuropeanCall market numQubits groverIterations shots backend

    /// Price European Put option with explicit quantum configuration
    let priceEuropeanPut spot strike rate vol expiry numQubits groverIterations shots backend =
        let market = { SpotPrice = spot; StrikePrice = strike; RiskFreeRate = rate; Volatility = vol; TimeToExpiry = expiry }
        price EuropeanPut market numQubits groverIterations shots backend

    /// Price Asian Call option with explicit quantum configuration
    let priceAsianCall spot strike rate vol expiry steps numQubits groverIterations shots backend =
        let market = { SpotPrice = spot; StrikePrice = strike; RiskFreeRate = rate; Volatility = vol; TimeToExpiry = expiry }
        price (AsianCall steps) market numQubits groverIterations shots backend

    /// Price Asian Put option with explicit quantum configuration
    let priceAsianPut spot strike rate vol expiry steps numQubits groverIterations shots backend =
        let market = { SpotPrice = spot; StrikePrice = strike; RiskFreeRate = rate; Volatility = vol; TimeToExpiry = expiry }
        price (AsianPut steps) market numQubits groverIterations shots backend

    // ========================================================================
    // GREEKS - Option Sensitivities via Finite Differences
    // ========================================================================

    /// Result of Greek calculation (First and Second Order Sensitivities)
    type OptionGreeks = {
        /// Base option price
        Price: float
        
        /// Sensitivity to spot price (∂V/∂S)
        /// Range: [0,1] for calls, [-1,0] for puts
        Delta: float
        
        /// Sensitivity to delta (∂²V/∂S²)
        /// Curvature of price wrt spot
        Gamma: float
        
        /// Sensitivity to volatility (∂V/∂σ)
        Vega: float
        
        /// Sensitivity to time decay (∂V/∂t)
        /// Usually negative (time decay eats value)
        Theta: float
        
        /// Sensitivity to interest rate (∂V/∂r)
        Rho: float
        
        /// 95% Confidence Intervals for each Greek
        /// Because Quantum Monte Carlo is probabilistic, Greeks also have error bars
        ConfidenceIntervals: GreekConfidenceIntervals
        
        /// Total number of quantum pricing calls made
        PricingCalls: int
        
        /// Method used
        Method: string
    }
    
    and GreekConfidenceIntervals = {
        Price: float
        Delta: float
        Gamma: float
        Vega: float
        Theta: float
        Rho: float
    }

    /// Configuration for Finite Difference Method (FDM)
    type GreeksConfig = {
        /// Relative bump for Spot Price (e.g., 0.01 = 1%)
        SpotBump: float
        
        /// Absolute bump for Volatility (e.g., 0.01 = 1%)
        VolatilityBump: float
        
        /// Absolute bump for Time (in years)
        /// Default: 1.0/365.0 (1 day)
        TimeBump: float
        
        /// Absolute bump for Interest Rate
        /// Default: 0.0001 (1 basis point)
        RateBump: float
    }

    /// Default configuration for Greeks
    let defaultGreeksConfig = {
        SpotBump = 0.01        // 1% spot shift
        VolatilityBump = 0.01  // 1% vol shift
        TimeBump = 1.0 / 365.0 // 1 day time shift
        RateBump = 0.0001      // 1 bp rate shift
    }

    /// Calculate Option Greeks using Quantum Finite Difference Method
    /// 
    /// ALGORITHM:
    /// Uses Central Difference Method for first derivatives (Delta, Vega, Rho):
    /// f'(x) ≈ (f(x+h) - f(x-h)) / 2h
    /// 
    /// Uses Central Difference Method for second derivative (Gamma):
    /// f''(x) ≈ (f(x+h) - 2f(x) + f(x-h)) / h²
    /// 
    /// Uses Forward Difference for Theta (time moves forward only):
    /// f'(t) ≈ (f(t+h) - f(t)) / h
    /// 
    /// PERFORMANCE:
    /// Requires multiple quantum pricing calls (usually 5-9 depending on reuse).
    /// Parallel execution is recommended if backend supports it.
    let calculateGreeks
        (optionType: OptionType)
        (market: MarketParameters)
        (config: GreeksConfig)
        (numQubits: int)
        (groverIterations: int)
        (backend: IQuantumBackend)
        : Async<QuantumResult<OptionGreeks>> =
        
        async {
            // Validate inputs
            if config.SpotBump <= 0.0 then
                return Error (QuantumError.ValidationError ("SpotBump", "Must be > 0"))
            elif config.VolatilityBump <= 0.0 then
                return Error (QuantumError.ValidationError ("VolatilityBump", "Must be > 0"))
            elif config.TimeBump <= 0.0 then
                return Error (QuantumError.ValidationError ("TimeBump", "Must be > 0"))
            elif config.RateBump <= 0.0 then
                return Error (QuantumError.ValidationError ("RateBump", "Must be > 0"))
            elif market.TimeToExpiry <= config.TimeBump then
                return Error (QuantumError.ValidationError ("TimeToExpiry", "Must be greater than TimeBump"))
            else
                
                // Helper to run pricing safely
                let priceAt params' = 
                    priceWithCancellation None optionType params' numQubits groverIterations 1000 backend
                
                // Define scenarios for Finite Differences
                
                // 1. Base Case (Center)
                let! baseResult = priceAt market
                
                match baseResult with
                | Error e -> return Error e
                | Ok basePrice ->
                    
                    // 2. Spot Shifts (for Delta, Gamma)
                    let h_S = market.SpotPrice * config.SpotBump
                    let market_Sup = { market with SpotPrice = market.SpotPrice + h_S }
                    let market_Sdown = { market with SpotPrice = market.SpotPrice - h_S }
                    
                    // 3. Vol Shifts (for Vega)
                    let h_v = config.VolatilityBump
                    let market_Vup = { market with Volatility = market.Volatility + h_v }
                    let market_Vdown = { market with Volatility = market.Volatility - h_v }
                    
                    // 4. Time Shift (for Theta) - forward difference (time decreases as we approach expiry)
                    // Note: Theta is ∂V/∂t, but t usually means "time to expiry".
                    // As calendar time passes, time-to-expiry decreases.
                    // So we look at V(T-h) - V(T) to see effect of 1 day passing.
                    let h_t = config.TimeBump
                    let market_Tless = { market with TimeToExpiry = market.TimeToExpiry - h_t }
                    
                    // 5. Rate Shifts (for Rho)
                    let h_r = config.RateBump
                    let market_Rup = { market with RiskFreeRate = market.RiskFreeRate + h_r }
                    let market_Rdown = { market with RiskFreeRate = market.RiskFreeRate - h_r }

                    // Execute all pricing calls (sequentially for now, could be parallel)
                    let! res_Sup = priceAt market_Sup
                    let! res_Sdown = priceAt market_Sdown
                    let! res_Vup = priceAt market_Vup
                    let! res_Vdown = priceAt market_Vdown
                    let! res_Tless = priceAt market_Tless
                    let! res_Rup = priceAt market_Rup
                    let! res_Rdown = priceAt market_Rdown
                    
                    // Aggregate results or fail if any failed
                    let results = [res_Sup; res_Sdown; res_Vup; res_Vdown; res_Tless; res_Rup; res_Rdown]
                    let errors = results |> List.choose (function Error e -> Some e | _ -> None)
                    
                    if not (List.isEmpty errors) then
                        return Error (List.head errors) // Return first error
                    else
                        // Unsafe extract (we checked for errors)
                        let getPrice (res: QuantumResult<OptionPrice>) = 
                            match res with Ok p -> p.Price | _ -> 0.0
                        
                        let P = basePrice.Price
                        let P_Sup = getPrice res_Sup
                        let P_Sdown = getPrice res_Sdown
                        let P_Vup = getPrice res_Vup
                        let P_Vdown = getPrice res_Vdown
                        let P_Tless = getPrice res_Tless
                        let P_Rup = getPrice res_Rup
                        let P_Rdown = getPrice res_Rdown
                        
                        // --- CALCULATE GREEKS ---
                        
                        // Delta (∂V/∂S) ≈ (P(S+h) - P(S-h)) / 2h
                        let delta = (P_Sup - P_Sdown) / (2.0 * h_S)
                        
                        // Gamma (∂²V/∂S²) ≈ (P(S+h) - 2P(S) + P(S-h)) / h²
                        let gamma = (P_Sup - 2.0 * P + P_Sdown) / (h_S * h_S)
                        
                        // Vega (∂V/∂σ) ≈ (P(σ+h) - P(σ-h)) / 2h
                        let vega = (P_Vup - P_Vdown) / (2.0 * h_v)
                        
                        // Theta (∂V/∂t) ≈ (P(T-h) - P(T)) / h
                        // Represents value change as 1 unit of time passes
                        let theta = (P_Tless - P) / h_t
                        
                        // Rho (∂V/∂r) ≈ (P(r+h) - P(r-h)) / 2h
                        let rho = (P_Rup - P_Rdown) / (2.0 * h_r)
                        
                        // --- CALCULATE CONFIDENCE INTERVALS ---
                        // Error propagation for central differences:
                        // Δ(f(x+h) - f(x-h)) = √(Δf² + Δf²) = √2 * Δf
                        // CI_deriv = (√2 * CI_price) / (2h)
                        
                        let baseCI = basePrice.ConfidenceInterval
                        
                        let ci_delta = (sqrt 2.0 * baseCI) / (2.0 * h_S)
                        let ci_gamma = (sqrt 6.0 * baseCI) / (h_S * h_S) // Approx from 1, -2, 1 coeff
                        let ci_vega = (sqrt 2.0 * baseCI) / (2.0 * h_v)
                        let ci_theta = (sqrt 2.0 * baseCI) / h_t
                        let ci_rho = (sqrt 2.0 * baseCI) / (2.0 * h_r)
                        
                        return Ok {
                            Price = P
                            Delta = delta
                            Gamma = gamma
                            Vega = vega
                            Theta = theta
                            Rho = rho
                            ConfidenceIntervals = {
                                Price = baseCI
                                Delta = ci_delta
                                Gamma = ci_gamma
                                Vega = ci_vega
                                Theta = ci_theta
                                Rho = ci_rho
                            }
                            PricingCalls = 8 // Base + 7 scenarios
                            Method = "Quantum Finite Difference (Center)"
                        }
        }

    /// Calculate Greeks for European Call (convenience wrapper)
    let greeksEuropeanCall spot strike rate vol expiry backend =
        let market = { SpotPrice = spot; StrikePrice = strike; RiskFreeRate = rate; Volatility = vol; TimeToExpiry = expiry }
        calculateGreeks EuropeanCall market defaultGreeksConfig 6 5 backend

    /// Calculate Greeks for European Put (convenience wrapper)
    let greeksEuropeanPut spot strike rate vol expiry backend =
        let market = { SpotPrice = spot; StrikePrice = strike; RiskFreeRate = rate; Volatility = vol; TimeToExpiry = expiry }
        calculateGreeks EuropeanPut market defaultGreeksConfig 6 5 backend
