// ==============================================================================
// Statistical Distributions — numerical PDF / CDF / quantile helpers
// ==============================================================================
// StatisticalDistributions provides deterministic, classical special-function
// helpers (error function, normal/log-normal PDF, CDF, and inverse-CDF/quantile)
// used to set up and post-process quantum sampling and finance workloads.
//
// These are pure functions — no quantum backend required. They are approximations
// (erf/erfc max error ~1.5e-7; normalQuantile max error ~1.1e-9); for higher
// precision use a dedicated numerics package such as Math.NET Numerics.
//
// Run:  dotnet fsi examples/Algorithms/StatisticalDistributionsExample.fsx
// ==============================================================================

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open FSharp.Azure.Quantum.Algorithms.StatisticalDistributions

printfn "Error function:"
printfn "  erf(1.0)  = %.6f   (≈ 0.842701)" (erf 1.0)
printfn "  erfc(1.0) = %.6f   (≈ 0.157299)" (erfc 1.0)
printfn ""

printfn "Standard normal N(0,1):"
printfn "  pdf(0)        = %.6f   (≈ 0.398942)" (normalPDF 0.0)
printfn "  cdf(0)        = %.6f   (= 0.5)" (normalCDF 0.0)
printfn "  cdf(1.96)     = %.6f   (≈ 0.975)" (normalCDF 1.96)
printfn "  quantile(.975)= %.6f   (≈ 1.96)" (normalQuantile 0.975)
printfn ""

printfn "General normal N(mean=10, sd=2):"
printfn "  cdf(12)       = %.6f" (normalCDFGeneral 10.0 2.0 12.0)
printfn "  quantile(.95) = %.6f" (normalQuantileGeneral 10.0 2.0 0.95)
printfn ""

printfn "Log-normal (mu=0, sigma=1):"
printfn "  pdf(1)        = %.6f" (logNormalPDF 0.0 1.0 1.0)
printfn "  cdf(1)        = %.6f   (= 0.5)" (logNormalCDF 0.0 1.0 1.0)
printfn ""

// Discretize a distribution into (representative-value, probability-weight) bins —
// handy for encoding a classical distribution into a quantum register.
printfn "Discretized standard normal (8 bins) — (value, weight):"
discretizeNormal 8
|> Array.iter (fun (value, weight) -> printfn "  % .4f  ->  %.4f" value weight)
