namespace FSharp.Azure.Quantum.Algorithms

/// Shor's Algorithm Type Definitions
///
/// Shared types used by both ShorsAlgorithm and ShorsBackendAdapter modules.
/// Separated to avoid circular dependencies in compilation.
module ShorsTypes =

    // ========================================================================
    // TYPES - Shor's configuration and results
    // ========================================================================

    /// Configuration for Shor's algorithm
    type ShorsConfig =
        {
            /// Number to factor (must be composite and > 2)
            NumberToFactor: int

            /// Random number a < N (coprime to N)
            /// If None, algorithm will choose random a
            RandomBase: int option

            /// Number of qubits for QPE precision
            /// Recommendation: 2 * (log₂ N) + 3 qubits
            PrecisionQubits: int

            /// Maximum attempts to find valid period
            MaxAttempts: int
        }

    /// Result of period-finding (quantum subroutine)
    [<Struct>]
    type PeriodFindingResult =
        {
            /// Found period r such that a^r ≡ 1 (mod N)
            Period: int

            /// Base a used in modular exponentiation
            Base: int

            /// QPE phase estimate
            PhaseEstimate: float

            /// Number of QPE attempts made
            Attempts: int
        }

    /// How the factors of a Shor's algorithm result were obtained.
    [<RequireQualifiedAccess>]
    type FactorSource =
        /// From a period measured by quantum period finding (QPE on modular exponentiation).
        | QuantumPeriodFinding

        /// By classical preprocessing, before any period finding: N is even, or the drawn base
        /// shares a factor with N (gcd(a, N) > 1). No quantum circuit ran for this result.
        | ClassicalPreprocessing

        /// No factors: N is prime or too small, or every attempt failed.
        | NotFactored

    /// Result of Shor's algorithm execution
    type ShorsResult =
        {
            /// Input number that was factored
            Number: int

            /// Found factors (p, q) such that N = p × q
            Factors: (int * int) option

            /// How the factors were obtained. Factors from classical preprocessing come with
            /// no PeriodResult: no period was measured for them.
            FactorSource: FactorSource

            /// Period-finding result
            PeriodResult: PeriodFindingResult option

            /// Whether factorization succeeded
            Success: bool

            /// Execution details/error message
            Message: string

            /// Configuration used
            Config: ShorsConfig
        }
