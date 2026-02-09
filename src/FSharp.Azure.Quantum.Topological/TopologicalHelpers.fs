namespace FSharp.Azure.Quantum.Topological

/// Shared helper functions for topological quantum computing modules.
///
/// Provides common complex number utilities and display formatting
/// used across R-matrix, F-matrix, and braiding operator modules.
module TopologicalHelpers =

    open System
    open System.Numerics

    // ========================================================================
    // COMPLEX NUMBER HELPERS
    // ========================================================================

    /// Create complex number from polar form: r * e^(iθ)
    let inline polar (r: float) (theta: float) : Complex =
        Complex(r * cos theta, r * sin theta)

    /// Create unit complex number: e^(iθ)
    let inline expI (theta: float) : Complex =
        polar 1.0 theta

    /// Pi constant for readability
    let π = Math.PI

    // ========================================================================
    // DISPLAY FORMATTING
    // ========================================================================

    /// Format a particle for display using standard topological notation
    let formatParticle (p: AnyonSpecies.Particle) : string =
        match p with
        | AnyonSpecies.Particle.Vacuum -> "1"
        | AnyonSpecies.Particle.Sigma -> "σ"
        | AnyonSpecies.Particle.Psi -> "ψ"
        | AnyonSpecies.Particle.Tau -> "τ"
        | _ -> p.ToString()

    /// Format a complex number for display
    let formatComplex (z: Complex) : string =
        let re, im = z.Real, z.Imaginary
        match abs re < 1e-10, abs im < 1e-10 with
        | true, true -> "0"
        | true, false -> sprintf "%.6fi" im
        | false, true -> sprintf "%.6f" re
        | false, false -> sprintf "%.6f + %.6fi" re im
