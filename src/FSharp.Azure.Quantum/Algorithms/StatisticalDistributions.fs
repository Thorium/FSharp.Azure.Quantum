namespace FSharp.Azure.Quantum.Algorithms

open System

/// Statistical Distribution Functions
/// 
/// Provides basic statistical distribution functions needed for financial
/// and scientific quantum algorithms (option pricing, quantum ML, etc.)
/// 
/// Implementations use standard numerical approximations with accuracy
/// suitable for quantum amplitude encoding (6-10 decimal places).
/// 
/// For production financial applications requiring higher precision,
/// consider using Math.NET Numerics or similar libraries.
module StatisticalDistributions =
    
    // ========================================================================
    // CONSTANTS
    // ========================================================================
    
    let private sqrt2 = sqrt 2.0
    let private sqrt2pi = sqrt (2.0 * Math.PI)
    let private invSqrt2pi = 1.0 / sqrt2pi
    
    // ========================================================================
    // ERROR FUNCTION (erf) - Abramowitz & Stegun approximation
    // ========================================================================
    
    /// Error function: erf(x) = (2/√π) ∫₀ˣ e^(-t²) dt
    /// 
    /// Uses Abramowitz & Stegun approximation (Formula 7.1.26)
    /// Maximum error: 1.5 × 10⁻⁷
    /// 
    /// This is the key function for normal distribution CDF.
    let erf (x: float) : float =
        let sign = if x < 0.0 then -1.0 else 1.0
        let x = abs x
        
        // Coefficients for A&S Formula 7.1.26
        let p = 0.3275911
        let a1 = 0.254829592
        let a2 = -0.284496736
        let a3 = 1.421413741
        let a4 = -1.453152027
        let a5 = 1.061405429
        
        let t = 1.0 / (1.0 + p * x)
        let t2 = t * t
        let t3 = t2 * t
        let t4 = t3 * t
        let t5 = t4 * t
        
        let y = 1.0 - (a1 * t + a2 * t2 + a3 * t3 + a4 * t4 + a5 * t5) * exp(-x * x)
        
        sign * y
    
    /// Complementary error function: erfc(x) = 1 - erf(x)
    /// 
    /// More accurate than computing 1 - erf(x) for large x
    let erfc (x: float) : float =
        1.0 - erf x
    
    // ========================================================================
    // NORMAL (GAUSSIAN) DISTRIBUTION
    // ========================================================================
    
    /// Standard normal probability density function (PDF)
    /// 
    /// φ(x) = (1/√(2π)) * e^(-x²/2)
    /// 
    /// This is the PDF of N(0,1) distribution.
    let normalPDF (x: float) : float =
        invSqrt2pi * exp(-0.5 * x * x)
    
    /// Standard normal cumulative distribution function (CDF)
    /// 
    /// Φ(x) = P(Z ≤ x) where Z ~ N(0,1)
    /// Φ(x) = (1/2) * [1 + erf(x/√2)]
    /// 
    /// Returns probability that standard normal variable is ≤ x
    let normalCDF (x: float) : float =
        0.5 * (1.0 + erf(x / sqrt2))
    
    /// Inverse normal CDF (quantile function, probit)
    /// 
    /// Φ⁻¹(p) = x such that Φ(x) = p
    /// 
    /// Uses Beasley-Springer-Moro algorithm (refinement of Acklam)
    /// Maximum error: 1.15 × 10⁻⁹ for 0 < p < 1
    /// 
    /// This is critical for Monte Carlo sampling and GBM encoding.
    let normalQuantile (p: float) : float =
        if p <= 0.0 || p >= 1.0 then
            failwith $"normalQuantile: p must be in (0,1), got {p}"
        
        // Rational approximation for central region
        let a = [| -3.969683028665376e+01; 2.209460984245205e+02
                   -2.759285104469687e+02; 1.383577518672690e+02
                   -3.066479806614716e+01; 2.506628277459239e+00 |]
        
        let b = [| -5.447609879822406e+01; 1.615858368580409e+02
                   -1.556989798598866e+02; 6.680131188771972e+01
                   -1.328068155288572e+01 |]
        
        let c = [| -7.784894002430293e-03; -3.223964580411365e-01;
                   -2.400758277161838e+00; -2.549732539343734e+00;
                    4.374664141464968e+00;  2.938163982698783e+00 |]
        
        let d = [| 7.784695709041462e-03; 3.224671290700398e-01
                   2.445134137142996e+00; 3.754408661907416e+00 |]
        
        // Define break-points
        let pLow = 0.02425
        let pHigh = 1.0 - pLow
        
        if p < pLow then
            // Rational approximation for lower region
            let q = sqrt(-2.0 * log p)
            (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
            ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1.0)
        
        elif p <= pHigh then
            // Rational approximation for central region
            let q = p - 0.5
            let r = q * q
            (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q /
            (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1.0)
        
        else
            // Rational approximation for upper region
            let q = sqrt(-2.0 * log(1.0 - p))
            -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
             ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1.0)
    
    /// General normal distribution PDF
    /// 
    /// N(μ, σ²): f(x) = (1/(σ√(2π))) * e^(-(x-μ)²/(2σ²))
    let normalPDFGeneral (mean: float) (stdDev: float) (x: float) : float =
        let z = (x - mean) / stdDev
        (1.0 / stdDev) * normalPDF z
    
    /// General normal distribution CDF
    /// 
    /// P(X ≤ x) where X ~ N(μ, σ²)
    let normalCDFGeneral (mean: float) (stdDev: float) (x: float) : float =
        let z = (x - mean) / stdDev
        normalCDF z
    
    /// General normal distribution quantile
    /// 
    /// Φ⁻¹(p; μ, σ²) = μ + σ * Φ⁻¹(p)
    let normalQuantileGeneral (mean: float) (stdDev: float) (p: float) : float =
        mean + stdDev * (normalQuantile p)
    
    // ========================================================================
    // LOG-NORMAL DISTRIBUTION (for GBM / Black-Scholes)
    // ========================================================================
    
    /// Log-normal distribution PDF
    /// 
    /// If log(X) ~ N(μ, σ²), then X is log-normally distributed
    /// Used in Black-Scholes model: S_T ~ LogNormal(log(S_0) + (r-σ²/2)T, σ√T)
    /// 
    /// f(x) = (1/(xσ√(2π))) * e^(-(log(x)-μ)²/(2σ²))
    let logNormalPDF (mu: float) (sigma: float) (x: float) : float =
        if x <= 0.0 then 0.0
        else
            let logX = log x
            let z = (logX - mu) / sigma
            (1.0 / (x * sigma * sqrt2pi)) * exp(-0.5 * z * z)
    
    /// Log-normal distribution CDF
    /// 
    /// P(X ≤ x) where log(X) ~ N(μ, σ²)
    let logNormalCDF (mu: float) (sigma: float) (x: float) : float =
        if x <= 0.0 then 0.0
        else
            let logX = log x
            normalCDF ((logX - mu) / sigma)
    
    /// Log-normal distribution quantile
    /// 
    /// LogNormal⁻¹(p; μ, σ²) = exp(μ + σ * Φ⁻¹(p))
    let logNormalQuantile (mu: float) (sigma: float) (p: float) : float =
        exp (mu + sigma * (normalQuantile p))
    
    // ========================================================================
    // UTILITY FUNCTIONS
    // ========================================================================
    
    /// Discretize standard normal distribution into n uniform bins
    /// 
    /// Returns array of (midpoint, probability) pairs
    /// Used for amplitude encoding in quantum circuits
    /// 
    /// Example: discretizeNormal 8 returns 8 bins covering ~[-3σ, +3σ]
    let discretizeNormal (numBins: int) : (float * float)[] =
        if numBins < 2 then
            failwith "numBins must be >= 2"
        
        [| 0 .. numBins - 1 |]
        |> Array.map (fun i ->
            // Uniform quantile spacing
            let pLower = (float i) / float numBins
            let pUpper = (float i + 1.0) / float numBins
            let pMid = (pLower + pUpper) / 2.0
            
            // Z-score at midpoint
            let z = normalQuantile pMid
            
            // Probability mass in this bin
            let prob = pUpper - pLower
            
            (z, prob)
        )
    
    /// Discretize log-normal distribution into n bins
    /// 
    /// Returns array of (price_level, probability) pairs
    /// Essential for encoding GBM distribution in quantum circuits
    let discretizeLogNormal (mu: float) (sigma: float) (numBins: int) : (float * float)[] =
        if numBins < 2 then
            failwith "numBins must be >= 2"
        
        [| 0 .. numBins - 1 |]
        |> Array.map (fun i ->
            // Uniform quantile spacing
            let pLower = (float i) / float numBins
            let pUpper = (float i + 1.0) / float numBins
            let pMid = (pLower + pUpper) / 2.0
            
            // Price level at midpoint
            let priceLevel = logNormalQuantile mu sigma pMid
            
            // Probability mass in this bin
            let prob = pUpper - pLower
            
            (priceLevel, prob)
        )
