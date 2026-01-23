/// Quantum Fourier Transform (QFT) Tutorial
/// 
/// A Li Tan-style pedagogical tutorial with worked numerical examples,
/// classical comparisons, and progressive exercises.
/// 
/// Inspired by "Digital Signal Processing: Fundamentals and Applications" (Li Tan)
/// - Practical-first approach with real numbers
/// - Step-by-step worked examples with solutions
/// - Progressive exercises from basic to advanced
/// - Comparison with classical methods

(*
===============================================================================
 Chapter 1: What is the Quantum Fourier Transform?
===============================================================================

MOTIVATION: The Discrete Fourier Transform in Classical Signal Processing
--------------------------------------------------------------------------
In classical signal processing, the Discrete Fourier Transform (DFT) converts
a time-domain signal into its frequency components. For a sequence of N samples
{x₀, x₁, ..., x_{N-1}}, the DFT produces frequency coefficients:

    X_k = Σⱼ₌₀^{N-1} xⱼ · e^{-2πijk/N}    for k = 0, 1, ..., N-1

The DFT is fundamental to audio processing (MP3), image compression (JPEG),
telecommunications, and medical imaging. However, the naive DFT requires O(N²)
operations. The Fast Fourier Transform (FFT) reduces this to O(N log N), but
for quantum systems with N = 2ⁿ states, even FFT becomes intractable.

THE QUANTUM ADVANTAGE
---------------------
The Quantum Fourier Transform (QFT) performs the same mathematical operation
on quantum amplitudes, but with only O(n²) gates for n qubits representing
N = 2ⁿ amplitudes. This is EXPONENTIALLY faster than classical methods:

    | n qubits | N = 2ⁿ states | Classical FFT | Quantum QFT |
    |----------|---------------|---------------|-------------|
    |    10    |      1,024    |    10,240     |     100     |
    |    20    |  1,048,576    | 20,971,520    |     400     |
    |    50    |   ~10^15      |   ~10^17      |    2,500    |

This exponential speedup is why QFT is the core subroutine in:
- Shor's algorithm for integer factorization
- Quantum Phase Estimation for chemistry simulation
- Period finding for cryptanalysis
- Quantum signal processing

MATHEMATICAL DEFINITION
-----------------------
The QFT transforms computational basis states according to:

    QFT|j⟩ = (1/√N) Σₖ₌₀^{N-1} e^{2πijk/N} |k⟩

where N = 2ⁿ for n qubits. Equivalently, in amplitude notation:

    |ψ⟩ = Σⱼ xⱼ|j⟩  →  QFT|ψ⟩ = Σₖ Xₖ|k⟩

    where X_k = (1/√N) Σⱼ xⱼ · e^{2πijk/N}

NOTE: The QFT uses e^{+2πijk/N} (positive exponent), while the classical DFT
typically uses e^{-2πijk/N} (negative). This is a convention choice - both are
valid Fourier transforms, just with opposite "frequency direction."

The QFT is UNITARY: QFT · QFT† = I (identity). This means information is
preserved and the transform is reversible.

IMPORTANT: QFT vs Classical FFT
-------------------------------
QFT operates on quantum AMPLITUDES, not classical data. Key differences:
- You cannot "load" arbitrary classical data into quantum amplitudes efficiently
- Measurement collapses the superposition - you get ONE outcome, not all N values
- The speedup is realized only in algorithms that use INTERFERENCE (Shor's, QPE)
- QFT is a SUBROUTINE, not a drop-in replacement for classical FFT

References:
  [1] Nielsen & Chuang, "Quantum Computation and Quantum Information",
      Cambridge University Press (2010), Section 5.1.
  [2] Coppersmith, "An Approximate Fourier Transform Useful in Quantum Factoring",
      IBM Research Report RC 19642 (1994). https://arxiv.org/abs/quant-ph/0201067
  [3] Wikipedia: Quantum_Fourier_transform
      https://en.wikipedia.org/wiki/Quantum_Fourier_transform
  [4] Li Tan, "Digital Signal Processing: Fundamentals and Applications",
      Academic Press (2008), Chapters 3-4 (classical DFT background).
*)

#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

open System
open System.Numerics
open FSharp.Azure.Quantum.Algorithms.QFT
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

printfn "============================================================================="
printfn " QUANTUM FOURIER TRANSFORM TUTORIAL"
printfn " A Practical Guide with Worked Examples"
printfn "============================================================================="
printfn ""

(*
===============================================================================
 Chapter 2: The QFT Circuit - Step by Step
===============================================================================

CIRCUIT STRUCTURE
-----------------
The QFT circuit for n qubits consists of:
  1. Hadamard gates (H) on each qubit
  2. Controlled phase rotations (CP) between qubits
  3. Bit-reversal SWAP gates (optional, for standard ordering)

For 3 qubits, the circuit looks like:

    |q₀⟩ ─── H ─── CP(π/2) ─── CP(π/4) ─────────────────────×───
                     │            │                          │
    |q₁⟩ ────────────●─────── H ─┼───── CP(π/2) ────────────┼×──
                                 │        │                  ││
    |q₂⟩ ────────────────────────●─────── ● ────── H ───────×┼──
                                                             │
                                                          SWAP

Gate Count Formula:
  - Hadamard gates:          n
  - Controlled phases:       n(n-1)/2
  - SWAP gates:              ⌊n/2⌋
  - Total:                   n + n(n-1)/2 + ⌊n/2⌋ ≈ n²/2

PHASE ROTATION ANGLES
---------------------
The controlled phase rotation between qubit j (control) and qubit k (target)
where k > j applies the angle:

    θ = 2π / 2^(k-j+1) = π / 2^(k-j)

Example for 3 qubits:
  - CP(q₁ → q₀): θ = π/2  (90°)
  - CP(q₂ → q₀): θ = π/4  (45°)
  - CP(q₂ → q₁): θ = π/2  (90°)
*)

printfn "============================================================================="
printfn " EXAMPLE 2.1: Gate Count Calculation"
printfn "============================================================================="
printfn ""
printfn "PROBLEM: Calculate the number of gates required for QFT on 4 qubits."
printfn ""
printfn "SOLUTION:"
printfn ""
printfn "  n = 4 qubits"
printfn ""
printfn "  Step 1: Count Hadamard gates"
printfn "    H gates = n = 4"
printfn ""
printfn "  Step 2: Count controlled phase gates"
printfn "    CP gates = n(n-1)/2 = 4(3)/2 = 6"
printfn "    "
printfn "    Breakdown:"
printfn "      After H on q₀: CP from q₁, q₂, q₃ = 3 gates"
printfn "      After H on q₁: CP from q₂, q₃ = 2 gates"
printfn "      After H on q₂: CP from q₃ = 1 gate"
printfn "      Total: 3 + 2 + 1 = 6 gates"
printfn ""
printfn "  Step 3: Count SWAP gates (for bit reversal)"
printfn "    SWAP gates = ⌊n/2⌋ = ⌊4/2⌋ = 2"
printfn "    (Swap q₀↔q₃ and q₁↔q₂)"
printfn ""
printfn "  ANSWER: Total gates = 4 + 6 + 2 = 12 gates"
printfn ""

// Verify with the library function
let gateCount4Qubits = estimateGateCount 4 true
printfn "  Library verification: estimateGateCount 4 true = %d gates" gateCount4Qubits
printfn ""

printfn "  COMPARISON WITH CLASSICAL:"
printfn "    Classical DFT:  N² = (2⁴)² = 256 operations"
printfn "    Classical FFT:  N log₂ N = 16 × 4 = 64 operations"
printfn "    Quantum QFT:    n²/2 + 3n/2 = 12 gates"
printfn ""
printfn "  IMPORTANT CAVEAT:"
printfn "    QFT transforms quantum AMPLITUDES, not classical data."
printfn "    You cannot extract all N amplitudes - measurement collapses the state!"
printfn "    The speedup is realized in algorithms like Shor's and QPE where"
printfn "    interference concentrates probability on useful outcomes."
printfn ""
printfn "  NOTE: The quantum advantage grows EXPONENTIALLY with qubits!"
printfn ""

(*
===============================================================================
 Chapter 3: QFT on Computational Basis States - Worked Examples
===============================================================================

When QFT is applied to a computational basis state |j⟩, the result is an
equal superposition of all basis states with specific phase relationships:

    QFT|j⟩ = (1/√N) Σₖ e^{2πijk/N} |k⟩

Key insight: ALL output amplitudes have EQUAL magnitude (1/√N), but different
PHASES determined by the input j.
*)

printfn "============================================================================="
printfn " EXAMPLE 3.1: QFT on |0⟩ (3 qubits)"
printfn "============================================================================="
printfn ""
printfn "PROBLEM: Apply QFT to the state |0⟩ = |000⟩ with N = 2³ = 8."
printfn ""
printfn "SOLUTION:"
printfn ""
printfn "  For j = 0:"
printfn "    QFT|0⟩ = (1/√8) Σₖ e^{2πi·0·k/8} |k⟩"
printfn "           = (1/√8) Σₖ e^0 |k⟩"
printfn "           = (1/√8) Σₖ |k⟩"
printfn ""
printfn "  This is an EQUAL SUPERPOSITION with no phase differences!"
printfn ""
printfn "  Output amplitudes (all equal):"
for k in 0..7 do
    let phase = 0.0  // e^0 = 1
    let amplitude = 1.0 / sqrt 8.0
    printfn "    |%d⟩: amplitude = 1/√8 ≈ %.4f, phase = 0°" k amplitude
printfn ""
printfn "  Expected measurement probabilities: 1/8 = 12.5%% for each basis state"
printfn ""

// Run the actual QFT
let backend = LocalBackend() :> IQuantumBackend

printfn "  LIBRARY VERIFICATION:"
match execute 3 backend defaultConfig with
| Ok result ->
    // Measure multiple times to see distribution
    let measurements = QuantumState.measure result.FinalState 1000
    let counts = Array.zeroCreate 8
    for bits in measurements do
        let idx = bits |> Array.indexed |> Array.fold (fun acc (i, b) -> acc + (b <<< i)) 0
        counts.[idx] <- counts.[idx] + 1
    
    printfn "    Measurement results (1000 shots):"
    for k in 0..7 do
        let pct = float counts.[k] / 10.0
        printfn "      |%d⟩: %3d shots (%.1f%%)" k counts.[k] pct
    printfn ""
    printfn "    Gate count: %d" result.GateCount
    printfn "    Execution time: %.2f ms" result.ExecutionTimeMs
| Error err ->
    printfn "    Error: %A" err
printfn ""

printfn "============================================================================="
printfn " EXAMPLE 3.2: QFT on |1⟩ (3 qubits)"  
printfn "============================================================================="
printfn ""
printfn "PROBLEM: Apply QFT to the state |1⟩ = |001⟩ with N = 8."
printfn ""
printfn "SOLUTION:"
printfn ""
printfn "  For j = 1:"
printfn "    QFT|1⟩ = (1/√8) Σₖ e^{2πi·1·k/8} |k⟩"
printfn "           = (1/√8) Σₖ e^{πik/4} |k⟩"
printfn ""
printfn "  Output amplitudes (equal magnitude, varying phase):"
let omega = Complex.Exp(Complex(0.0, Math.PI / 4.0))  // e^{iπ/4} = 8th root of unity
for k in 0..7 do
    let phase_k = omega ** float k
    let angleDegrees = phase_k.Phase * 180.0 / Math.PI
    printfn "    |%d⟩: amplitude = (1/√8)·e^{i·%d·π/4} = %.3f + %.3fi, phase = %.0f°" 
        k k phase_k.Real phase_k.Imaginary angleDegrees
printfn ""
printfn "  Key observation: Amplitudes form a spiral pattern in the complex plane!"
printfn "  Each successive state has an additional phase rotation of π/4 (45°)."
printfn ""

printfn "  LIBRARY VERIFICATION:"
match transformBasisState 3 1 backend defaultConfig with
| Ok result ->
    let measurements = QuantumState.measure result.FinalState 1000
    let counts = Array.zeroCreate 8
    for bits in measurements do
        let idx = bits |> Array.indexed |> Array.fold (fun acc (i, b) -> acc + (b <<< i)) 0
        counts.[idx] <- counts.[idx] + 1
    
    printfn "    Measurement results (1000 shots):"
    for k in 0..7 do
        let pct = float counts.[k] / 10.0
        printfn "      |%d⟩: %3d shots (%.1f%%)" k counts.[k] pct
    printfn ""
    printfn "    Note: Equal probabilities (12.5%%) despite different phases!"
    printfn "    Phases affect INTERFERENCE, not individual measurement probabilities."
| Error err ->
    printfn "    Error: %A" err
printfn ""

printfn "============================================================================="
printfn " EXAMPLE 3.3: QFT on |5⟩ (3 qubits) - A More Complex Example"
printfn "============================================================================="
printfn ""
printfn "PROBLEM: Apply QFT to |5⟩ = |101⟩ (binary) with N = 8."
printfn ""
printfn "SOLUTION:"
printfn ""
printfn "  For j = 5:"
printfn "    QFT|5⟩ = (1/√8) Σₖ e^{2πi·5·k/8} |k⟩"
printfn "           = (1/√8) Σₖ e^{5πik/4} |k⟩"
printfn ""
printfn "  Phase calculation for each output state:"
for k in 0..7 do
    let totalPhase = (5.0 * float k * Math.PI / 4.0) % (2.0 * Math.PI)
    let phase_k = Complex.Exp(Complex(0.0, totalPhase))
    let angleDegrees = if totalPhase > Math.PI then (totalPhase - 2.0 * Math.PI) else totalPhase
    let angleDeg = angleDegrees * 180.0 / Math.PI
    printfn "    |%d⟩: phase = 5×%d×π/4 mod 2π = %.2fπ = %.0f°" k k (totalPhase / Math.PI) angleDeg
printfn ""

printfn "  LIBRARY VERIFICATION:"
match transformBasisState 3 5 backend defaultConfig with
| Ok result ->
    printfn "    QFT transformation successful!"
    printfn "    Gate count: %d" result.GateCount
    printfn "    State is normalized: %b" (QuantumState.isNormalized result.FinalState)
| Error err ->
    printfn "    Error: %A" err
printfn ""

(*
===============================================================================
 Chapter 4: The QFT-Inverse QFT Identity (Unitarity)
===============================================================================

Since QFT is unitary, applying QFT followed by its inverse (QFT†) returns
the original state:

    QFT† · QFT = I (identity)

This is analogous to the classical Fourier transform pair where transforming
to frequency domain and back recovers the original signal.

PHYSICAL INTERPRETATION:
- QFT: Time domain → Frequency domain
- QFT†: Frequency domain → Time domain

This property is essential for quantum algorithms:
- In Shor's algorithm: QFT finds the period, QFT† is implicit in measurement
- In QPE: QFT† extracts phase information into measurable basis states
*)

printfn "============================================================================="
printfn " EXAMPLE 4.1: Round-Trip Verification (QFT → QFT† = Identity)"
printfn "============================================================================="
printfn ""
printfn "PROBLEM: Verify that QFT followed by inverse QFT returns |0⟩ to |0⟩."
printfn ""
printfn "SOLUTION:"
printfn ""
printfn "  Step 1: Start with |000⟩"
printfn "  Step 2: Apply QFT → equal superposition"
printfn "  Step 3: Apply QFT† → should return to |000⟩"
printfn ""

printfn "  LIBRARY VERIFICATION:"
match verifyRoundTrip 3 backend with
| Ok isIdentity ->
    if isIdentity then
        printfn "    Result: PASSED"
        printfn "    All measurements returned |000⟩"
        printfn "    QFT · QFT† = Identity verified!"
    else
        printfn "    Result: FAILED"
        printfn "    Some measurements were not |000⟩"
| Error err ->
    printfn "    Error: %A" err
printfn ""

printfn "  UNITARITY VERIFICATION (with config options):"
for applySwaps in [true; false] do
    for inverse in [true; false] do
        let config = { defaultConfig with ApplySwaps = applySwaps; Inverse = inverse }
        let desc = sprintf "ApplySwaps=%b, Inverse=%b" applySwaps inverse
        match verifyUnitarity 3 backend config with
        | Ok isUnitary ->
            let status = if isUnitary then "PASS" else "FAIL"
            printfn "    %s: %s" desc status
        | Error err ->
            printfn "    %s: ERROR - %A" desc err
printfn ""

(*
===============================================================================
 Chapter 5: Applications of QFT
===============================================================================

APPLICATION 1: Period Finding (Foundation of Shor's Algorithm)
--------------------------------------------------------------
Given a function f(x) with period r (meaning f(x) = f(x+r)), QFT can find r
using quantum interference. This is the core of Shor's factoring algorithm.

APPLICATION 2: Quantum Phase Estimation (QPE)
---------------------------------------------
QPE uses inverse QFT to extract eigenvalues of unitary operators. Given
U|ψ⟩ = e^{2πiφ}|ψ⟩, QPE outputs an n-bit approximation of φ.

APPLICATION 3: Quantum Signal Processing
----------------------------------------
QFT enables frequency analysis of quantum states, analogous to classical
FFT but with exponential speedup for quantum data.
*)

printfn "============================================================================="
printfn " EXAMPLE 5.1: Complexity Comparison - Classical vs Quantum"
printfn "============================================================================="
printfn ""
printfn "  Problem: Transform a signal/state of size N = 2ⁿ"
printfn ""
printfn "  | n qubits |    N states   | Classical DFT | Classical FFT | Quantum QFT |"
printfn "  |----------|---------------|---------------|---------------|-------------|"
for n in [4; 8; 16; 20; 30] do
    let N = 1L <<< n
    let dft = if n <= 16 then (N * N).ToString("N0") else sprintf "~10^%d" (2 * n / 3)  // N² ≈ 2^(2n)
    let fft = if n <= 30 then (N * int64 n).ToString("N0") else sprintf "~10^%d" (n / 3)
    let qft = estimateGateCount n true
    let nStr = if n <= 20 then N.ToString("N0") else sprintf "~10^%d" (n / 3)
    printfn "  |    %2d    | %13s | %13s | %13s | %11d |" n nStr dft fft qft
printfn ""
printfn "  At n=50 qubits: QFT uses ~2,500 gates on ~10^15 amplitudes!"
printfn "  Classical computers cannot even STORE that many numbers."
printfn ""

printfn "============================================================================="
printfn " EXAMPLE 5.2: QFT in Shor's Algorithm Context"
printfn "============================================================================="
printfn ""
printfn "PROBLEM: Factor N=15 using Shor's algorithm (simplified overview)."
printfn ""
printfn "SOLUTION OUTLINE:"
printfn ""
printfn "  Step 1: Choose random a = 7 (coprime with 15)"
printfn "  Step 2: Find the period r of f(x) = 7^x mod 15"
printfn "          f(0)=1, f(1)=7, f(2)=4, f(3)=13, f(4)=1, ..."
printfn "          Period r = 4"
printfn ""
printfn "  Step 3: (QUANTUM) Use QFT to find period via interference"
printfn "          Prepare superposition: Σₓ |x⟩|7^x mod 15⟩"
printfn "          Apply QFT to first register"
printfn "          Measure → peaks at multiples of N/r"
printfn ""
printfn "  Step 4: Extract factors from period"
printfn "          gcd(7^{r/2} + 1, 15) = gcd(50, 15) = 5"
printfn "          gcd(7^{r/2} - 1, 15) = gcd(48, 15) = 3"
printfn ""
printfn "  ANSWER: 15 = 3 × 5"
printfn ""
printfn "  QFT is the CRITICAL component enabling quantum speedup!"
printfn "  Classical period finding: O(√N) ≈ O(e^{n/2}) for n-bit numbers"
printfn "  Quantum (with QFT):      O((log N)³) = O(n³) polynomial!"
printfn ""

(*
===============================================================================
 Chapter 6: Exercises
===============================================================================
*)

printfn "============================================================================="
printfn " EXERCISES"
printfn "============================================================================="
printfn ""
printfn "Exercise 1 (Basic): Gate Count Calculation"
printfn "-------------------------------------------"
printfn "Calculate the total gate count for QFT on 5 qubits."
printfn ""
printfn "  Hint: Use the formula from Example 2.1"
printfn "  Your answer: _____"
printfn ""
let answer1 = estimateGateCount 5 true
printfn "  Library answer: %d gates" answer1
printfn ""

printfn "Exercise 2 (Intermediate): Phase Calculation"
printfn "---------------------------------------------"
printfn "For QFT|3⟩ with 2 qubits (N=4), calculate the phase of each output amplitude."
printfn ""
printfn "  QFT|3⟩ = (1/√4) Σₖ e^{2πi·3·k/4} |k⟩"
printfn ""
printfn "  For k=0: phase = 2π·3·0/4 = ____"
printfn "  For k=1: phase = 2π·3·1/4 = ____"
printfn "  For k=2: phase = 2π·3·2/4 = ____"
printfn "  For k=3: phase = 2π·3·3/4 = ____"
printfn ""
printfn "  SOLUTION:"
for k in 0..3 do
    let phase = (2.0 * Math.PI * 3.0 * float k / 4.0) % (2.0 * Math.PI)
    let phasePi = phase / Math.PI
    printfn "    k=%d: phase = %.2fπ radians = %.0f°" k phasePi (phase * 180.0 / Math.PI)
printfn ""

printfn "Exercise 3 (Advanced): Verify QFT on All 2-Qubit Basis States"
printfn "--------------------------------------------------------------"
printfn "Apply QFT to each of |0⟩, |1⟩, |2⟩, |3⟩ and verify measurement statistics."
printfn ""
for j in 0..3 do
    printfn "  QFT|%d⟩:" j
    match transformBasisState 2 j backend defaultConfig with
    | Ok result ->
        let measurements = QuantumState.measure result.FinalState 400
        let counts = Array.zeroCreate 4
        for bits in measurements do
            let idx = bits |> Array.indexed |> Array.fold (fun acc (i, b) -> acc + (b <<< i)) 0
            counts.[idx] <- counts.[idx] + 1
        printfn "    Measurements: |0⟩=%d, |1⟩=%d, |2⟩=%d, |3⟩=%d" 
            counts.[0] counts.[1] counts.[2] counts.[3]
        printfn "    Expected: ~100 each (25%%)"
    | Error err ->
        printfn "    Error: %A" err
printfn ""

printfn "============================================================================="
printfn " SUMMARY: Key Takeaways"
printfn "============================================================================="
printfn ""
printfn "  1. QFT transforms computational basis to frequency basis"
printfn "     QFT|j⟩ = (1/√N) Σₖ e^{2πijk/N} |k⟩"
printfn ""
printfn "  2. Gate complexity: O(n²) for n qubits"
printfn "     - n Hadamard gates"
printfn "     - n(n-1)/2 controlled phase gates"  
printfn "     - n/2 SWAP gates (optional)"
printfn ""
printfn "  3. Exponential speedup over classical DFT/FFT"
printfn "     - Classical: O(N²) or O(N log N)"
printfn "     - Quantum: O((log N)²)"
printfn ""
printfn "  4. QFT is UNITARY: QFT · QFT† = Identity"
printfn ""
printfn "  5. Applications:"
printfn "     - Shor's algorithm (factoring)"
printfn "     - Quantum Phase Estimation"
printfn "     - Period finding"
printfn "     - Quantum signal processing"
printfn ""
printfn "============================================================================="
printfn " END OF TUTORIAL"
printfn "============================================================================="
