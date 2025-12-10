namespace FSharp.Azure.Quantum

open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Backends
open FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.Algorithms.ShorsTypes
open FSharp.Azure.Quantum.Algorithms.Shor

/// High-level Quantum Period Finder Builder - Shor's Algorithm Period Finding
/// 
/// DESIGN PHILOSOPHY:
/// This is a BUSINESS DOMAIN API for cryptography researchers and quantum educators
/// who want to demonstrate integer factorization using Shor's algorithm without
/// understanding QPE internals (phase estimation, inverse QFT, continued fractions).
/// 
/// SHOR'S ALGORITHM:
/// - Uses Quantum Phase Estimation to find period of modular exponentiation
/// - Classical post-processing extracts factors from period
/// - Exponentially faster than classical factorization algorithms
/// - Demonstrates quantum supremacy over classical RSA security
/// 
/// WHAT IS PERIOD FINDING:
/// Given composite N and random base a < N, find period r where:
///   a^r ≡ 1 (mod N)
/// Once r is found, factors can be extracted using gcd operations.
/// 
/// USE CASES:
/// - Integer factorization (breaking RSA keys)
/// - Cryptographic security analysis
/// - Quantum supremacy demonstrations
/// - Educational quantum computing courses
/// - Order finding in modular arithmetic
/// 
/// EXAMPLE USAGE:
///   // Simple: Factor N=15
///   let problem = periodFinder {
///       number 15
///       precision 8
///   }
///   
///   // Advanced: Custom base and attempts
///   let problem = periodFinder {
///       number 15
///       chosenBase 7        // Use specific base
///       precision 16        // Higher precision
///       maxAttempts 20      // More attempts
///       backend ionqBackend
///   }
///   
///   // Solve the problem
///   match solve problem with
///   | Ok result -> 
///       printfn "Period: %d" result.Period
///       match result.Factors with
///       | Some (p, q) -> printfn "Factors: %d × %d" p q
///       | None -> printfn "No factors found (try again)"
///   | Error msg -> printfn "Error: %s" msg
module QuantumPeriodFinder =
    
    // ============================================================================
    // CORE TYPES - Period Finding Domain Model
    // ============================================================================
    
    /// <summary>
    /// Complete quantum period-finding problem specification.
    /// Used to configure Shor's algorithm for integer factorization.
    /// </summary>
    type PeriodFinderProblem = {
        /// Number to factor (N > 3, composite)
        Number: int
        
        /// Random base a < N (coprime to N)
        /// If None, algorithm auto-selects random base
        Base: int option
        
        /// QPE precision qubits (recommended: 2*log₂(N) + 3)
        /// Higher precision = better success rate but more qubits
        Precision: int
        
        /// Maximum attempts to find valid period
        /// Algorithm may fail and retry with different base
        MaxAttempts: int
        
        /// Quantum backend to use (None = LocalBackend)
        Backend: BackendAbstraction.IQuantumBackend option
        
        /// Number of measurement shots for QPE (None = auto-scale: 1024 for Local, 2048 for Cloud)
        /// Higher shots = better phase estimate accuracy but more execution time
        Shots: int option
    }
    
    /// <summary>
    /// Result of quantum period finding and factorization.
    /// Contains both the mathematical results and execution metadata.
    /// </summary>
    type PeriodFinderResult = {
        /// Input number that was analyzed
        Number: int
        
        /// Found period r where a^r ≡ 1 (mod N)
        Period: int
        
        /// Base a used in modular exponentiation
        Base: int
        
        /// Found factors (p, q) such that N = p × q
        /// None if factorization failed or N is prime
        Factors: (int * int) option
        
        /// QPE phase estimate (s/r where s is measured phase)
        PhaseEstimate: float
        
        /// Total qubits used (precision + log₂(N))
        QubitsUsed: int
        
        /// Number of attempts made to find period
        Attempts: int
        
        /// Whether factorization succeeded
        Success: bool
        
        /// Backend used for execution
        BackendName: string
        
        /// Execution details or error message
        Message: string
    }
    
    // ============================================================================
    // VALIDATION HELPERS
    // ============================================================================
    
    /// <summary>
    /// Validates a period-finding problem specification.
    /// Checks number range, precision bounds, and configuration validity.
    /// </summary>
    let private validate (problem: PeriodFinderProblem) : Result<unit, QuantumError> =
        // Check number is valid for factorization
        if problem.Number < 4 then
            Error (QuantumError.ValidationError("Number", "must be at least 4 for factorization"))
        elif problem.Number > 10000 then
            Error (QuantumError.ValidationError("Number", "exceeds simulation limit (10000) for period finding"))
        
        // Check precision
        elif problem.Precision < 1 then
            Error (QuantumError.ValidationError("Precision", "must be at least 1 qubit"))
        elif problem.Precision > 20 then
            Error (QuantumError.ValidationError("Precision", "exceeds practical limit (20 qubits) for NISQ devices"))
        
        // Check attempts
        elif problem.MaxAttempts < 1 then
            Error (QuantumError.ValidationError("MaxAttempts", "must be at least 1"))
        elif problem.MaxAttempts > 100 then
            Error (QuantumError.ValidationError("MaxAttempts", "exceeds reasonable limit (100)"))
        
        // Check base if specified
        elif problem.Base.IsSome then
            let baseVal = problem.Base.Value
            if baseVal < 2 then
                Error (QuantumError.ValidationError("Base", "must be at least 2"))
            elif baseVal >= problem.Number then
                Error (QuantumError.ValidationError("Base", "must be less than Number"))
            else
                Ok ()
        else
            Ok ()
    
    // ============================================================================
    // COMPUTATION EXPRESSION BUILDER
    // ============================================================================
    
    /// <summary>
    /// Computation expression builder for quantum period finding problems.
    /// Provides F#-idiomatic DSL for constructing Shor's algorithm configurations.
    /// </summary>
    type PeriodFinderBuilder() =
        
        /// Default problem (user must specify number and precision)
        let defaultProblem = {
            Number = 15              // Default to classic example N=15
            Base = None              // Auto-select random base
            Precision = 8            // Reasonable default precision
            MaxAttempts = 10         // Standard retry count
            Backend = None           // Use local simulator
            Shots = None             // Auto-scale based on backend
        }
        
        /// Initialize builder with default problem
        member _.Yield(_) = defaultProblem
        
        /// Set number to factor
        [<CustomOperation("number")>]
        member _.Number(problem: PeriodFinderProblem, n: int) : PeriodFinderProblem =
            { problem with Number = n }
        
        /// Set random base for modular exponentiation
        [<CustomOperation("chosenBase")>]
        member _.ChosenBase(problem: PeriodFinderProblem, a: int) : PeriodFinderProblem =
            { problem with Base = Some a }
        
        /// Set QPE precision qubits
        [<CustomOperation("precision")>]
        member _.Precision(problem: PeriodFinderProblem, p: int) : PeriodFinderProblem =
            { problem with Precision = p }
        
        /// Set maximum attempts
        [<CustomOperation("maxAttempts")>]
        member _.MaxAttempts(problem: PeriodFinderProblem, attempts: int) : PeriodFinderProblem =
            { problem with MaxAttempts = attempts }
        
        /// Set quantum backend
        [<CustomOperation("backend")>]
        member _.Backend(problem: PeriodFinderProblem, backend: BackendAbstraction.IQuantumBackend) : PeriodFinderProblem =
            { problem with Backend = Some backend }
        
        /// <summary>
        /// Set the number of measurement shots for quantum phase estimation.
        /// Higher shot counts improve phase estimate accuracy but increase execution time.
        /// </summary>
        /// <param name="shots">Number of measurements (typical: 1024-4096)</param>
        /// <remarks>
        /// If not specified, auto-scales based on backend:
        /// - LocalBackend: 1024 shots
        /// - Cloud backends: 2048 shots
        /// </remarks>
        [<CustomOperation("shots")>]
        member _.Shots(problem: PeriodFinderProblem, shots: int) : PeriodFinderProblem =
            { problem with Shots = Some shots }
        
        /// Finalize and validate the problem
        member _.Run(problem: PeriodFinderProblem) : Result<PeriodFinderProblem, QuantumError> =
            validate problem |> Result.map (fun _ -> problem)
    
    /// Global computation expression instance for period finding
    let periodFinder = PeriodFinderBuilder()
    
    // ============================================================================
    // SOLVER FUNCTION
    // ============================================================================
    
    /// Solve quantum period-finding problem using Shor's algorithm
    /// 
    /// Executes:
    ///   1. Validate problem configuration
    ///   2. Create ShorsConfig from problem
    ///   3. Execute Shor's algorithm via Shor.execute
    ///   4. Map result to PeriodFinderResult
    /// 
    /// Example:
    ///   let problem = periodFinder {
    ///       number 15
    ///       precision 8
    ///   }
    ///   match solve problem with
    ///   | Ok result -> printfn "Period: %d, Factors: %A" result.Period result.Factors
    ///   | Error err -> printfn "Error: %s" err.Message
    let solve (problem: PeriodFinderProblem) : Result<PeriodFinderResult, QuantumError> =
        result {
            // Validate problem first
            do! validate problem
            
            // Convert to ShorsConfig
            let config = {
                NumberToFactor = problem.Number
                RandomBase = problem.Base
                PrecisionQubits = problem.Precision
                MaxAttempts = problem.MaxAttempts
            }
            
            // Get backend (required by Rule 1)
            let actualBackend = 
                match problem.Backend with
                | Some backend -> backend
                | None -> LocalBackend.LocalBackend() :> BackendAbstraction.IQuantumBackend
            
            // Execute Shor's algorithm with backend
            let! shorsResult = execute config actualBackend
            
            // Determine backend name
            let backendName = 
                match problem.Backend with
                | Some backend -> backend.GetType().Name
                | None -> "LocalSimulator"
            
            // Calculate qubits used (precision + log₂(N))
            let qubitsUsed = 
                problem.Precision + int (ceil (log (float problem.Number) / log 2.0))
            
            // Extract period and phase from PeriodResult
            let (period, baseUsed, phaseEst, attempts) =
                match shorsResult.PeriodResult with
                | Some pr -> (pr.Period, pr.Base, pr.PhaseEstimate, pr.Attempts)
                | None -> 
                    // Failed to find period - use defaults
                    (0, problem.Base |> Option.defaultValue 2, 0.0, problem.MaxAttempts)
            
            return {
                Number = problem.Number
                Period = period
                Base = baseUsed
                Factors = shorsResult.Factors
                PhaseEstimate = phaseEst
                QubitsUsed = qubitsUsed
                Attempts = attempts
                Success = shorsResult.Success
                BackendName = backendName
                Message = shorsResult.Message
            }
        }
    
    // ============================================================================
    // CONVENIENCE HELPERS
    // ============================================================================
    
    /// Quick helper for simple factorization with defaults
    /// 
    /// Example: factorInteger 15 8
    let factorInteger (n: int) (p: int) : Result<PeriodFinderProblem, QuantumError> =
        periodFinder {
            number n
            precision p
        }
    
    /// Quick helper for factorization with custom base
    /// 
    /// Example: factorIntegerWithBase 15 7 8
    let factorIntegerWithBase (n: int) (baseVal: int) (p: int) : Result<PeriodFinderProblem, QuantumError> =
        periodFinder {
            number n
            chosenBase baseVal
            precision p
        }
    
    /// Quick helper for RSA breaking demonstration
    /// Uses recommended precision (2*log₂(N) + 3) for high success rate
    /// 
    /// Example: breakRSA 15  // N = 15 (p=3, q=5)
    let breakRSA (rsaModulus: int) : Result<PeriodFinderProblem, QuantumError> =
        let recommendedPrecision = 
            2 * int (ceil (log (float rsaModulus) / log 2.0)) + 3
        
        periodFinder {
            number rsaModulus
            precision recommendedPrecision
            maxAttempts 20  // Higher attempts for RSA breaking
        }
    
    /// Estimate resource requirements without executing
    /// Returns human-readable string with qubit and gate estimates
    let estimateResources (number: int) (precision: int) : string =
        let nQubits = int (ceil (log (float number) / log 2.0))
        let totalQubits = precision + nQubits
        
        // QPE circuit depth estimate
        let qpeDepth = precision * nQubits * 2  // Approximate depth
        let gateCount = precision * nQubits * 10  // Rough gate count
        
        sprintf """Quantum Period Finding Resource Estimate:
  Number to Factor: %d
  Precision Qubits: %d
  Register Qubits: %d (⌈log₂(%d)⌉)
  Total Qubits: %d
  Estimated Gates: ~%d (QPE + modular exponentiation)
  Estimated Depth: ~%d
  Feasibility: %s
  Classical Complexity: O(e^(log N)^(1/3)) ~ %d operations
  Quantum Speedup: Exponential (polynomial vs exponential)"""
            number
            precision
            nQubits
            number
            totalQubits
            gateCount
            qpeDepth
            (if totalQubits <= 20 then "✓ Feasible on NISQ devices" else "✗ Requires fault-tolerant quantum computer")
            (pown number 3)  // Rough classical estimate
    
    /// Export result to human-readable string
    let describeResult (result: PeriodFinderResult) : string =
        let factorsText =
            match result.Factors with
            | Some (p, q) -> sprintf "Found: %d = %d × %d" result.Number p q
            | None -> "No factors found (may need retry with different base)"
        
        let successIcon = if result.Success then "✓" else "✗"
        
        sprintf """=== Quantum Period Finding Result ===
%s Success: %s

Input:
  Number: %d
  Base: %d (random coprime base)
  Precision: %d qubits

Quantum Result:
  Period: %d (a^%d ≡ 1 mod %d)
  Phase Estimate: %.6f
  QPE Attempts: %d

Factorization:
  %s

Resources:
  Qubits Used: %d
  Backend: %s

Details: %s"""
            successIcon
            (if result.Success then "Factorization succeeded" else "Factorization failed")
            result.Number
            result.Base
            result.QubitsUsed
            result.Period
            result.Period
            result.Number
            result.PhaseEstimate
            result.Attempts
            factorsText
            result.QubitsUsed
            result.BackendName
            result.Message
