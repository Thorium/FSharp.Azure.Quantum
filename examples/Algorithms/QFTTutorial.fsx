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
{xâ‚€, xâ‚, ..., x_{N-1}}, the DFT produces frequency coefficients:

    X_k = Î£â±¼â‚Œâ‚€^{N-1} xâ±¼ Â· e^{-2Ï€ijk/N}    for k = 0, 1, ..., N-1

The DFT is fundamental to audio processing (MP3), image compression (JPEG),
telecommunications, and medical imaging. However, the naive DFT requires O(NÂ²)
operations. The Fast Fourier Transform (FFT) reduces this to O(N log N), but
for quantum systems with N = 2â¿ states, even FFT becomes intractable.

THE QUANTUM ADVANTAGE
---------------------
The Quantum Fourier Transform (QFT) performs the same mathematical operation
on quantum amplitudes, but with only O(nÂ²) gates for n qubits representing
N = 2â¿ amplitudes. This is EXPONENTIALLY faster than classical methods:

    | n qubits | N = 2â¿ states | Classical FFT | Quantum QFT |
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

    QFT|jâŸ© = (1/âˆšN) Î£â‚–â‚Œâ‚€^{N-1} e^{2Ï€ijk/N} |kâŸ©

where N = 2â¿ for n qubits. Equivalently, in amplitude notation:

    |ÏˆâŸ© = Î£â±¼ xâ±¼|jâŸ©  â†’  QFT|ÏˆâŸ© = Î£â‚– Xâ‚–|kâŸ©

    where X_k = (1/âˆšN) Î£â±¼ xâ±¼ Â· e^{2Ï€ijk/N}

NOTE: The QFT uses e^{+2Ï€ijk/N} (positive exponent), while the classical DFT
typically uses e^{-2Ï€ijk/N} (negative). This is a convention choice - both are
valid Fourier transforms, just with opposite "frequency direction."

The QFT is UNITARY: QFT Â· QFTâ€  = I (identity). This means information is
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

#r "nuget: Microsoft.Extensions.Logging.Abstractions, 10.0.0"
#r "../../src/FSharp.Azure.Quantum/bin/Debug/net10.0/FSharp.Azure.Quantum.dll"

#load "../_common/Cli.fs"
#load "../_common/Data.fs"
#load "../_common/Reporting.fs"

open System
open System.Numerics
open FSharp.Azure.Quantum.Algorithms.QFT
open FSharp.Azure.Quantum.Backends.LocalBackend
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Examples.Common

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

let argv = fsi.CommandLineArgs |> Array.skip 1
let args = Cli.parse argv

Cli.exitIfHelp
    "QFTTutorial.fsx"
    "Quantum Fourier Transform tutorial with worked examples."
    [ { Cli.OptionSpec.Name = "example"
        Description = "Example to run: gates|qft0|qft1|qft5|roundtrip|complexity|exercises|all"
        Default = Some "all" }
      { Cli.OptionSpec.Name = "qubits"
        Description = "Number of qubits for QFT examples"
        Default = Some "3" }
      { Cli.OptionSpec.Name = "output"
        Description = "Write results to JSON file"
        Default = None }
      { Cli.OptionSpec.Name = "csv"
        Description = "Write results to CSV file"
        Default = None }
      { Cli.OptionSpec.Name = "quiet"
        Description = "Suppress printed output"
        Default = None } ]
    args

let quiet = Cli.hasFlag "quiet" args
let exampleName = Cli.getOr "example" "all" args
let numQubits = Cli.getIntOr "qubits" 3 args
let outputPath = Cli.tryGet "output" args
let csvPath = Cli.tryGet "csv" args

let backend = LocalBackend() :> IQuantumBackend
let results = ResizeArray<Map<string, string>>()
let shouldRun name = exampleName = "all" || exampleName = name

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/// Decode a measurement bitstring to an integer index.
let bitsToIndex (bits: int[]) : int =
    bits |> Array.indexed |> Array.fold (fun acc (i, b) -> acc + (b <<< i)) 0

/// Measure a quantum state multiple times and return counts per basis state.
let measureCounts (state: QuantumState) (nStates: int) (shots: int) : int[] =
    let measurements = QuantumState.measure state shots
    let counts = Array.zeroCreate nStates
    for bits in measurements do
        let idx = bitsToIndex bits
        if idx < nStates then
            counts.[idx] <- counts.[idx] + 1
    counts

if not quiet then
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

    |q0âŸ© --- H --- CP(pi/2) --- CP(pi/4) -----------------x---
                     |            |                        |
    |q1âŸ© ------------*------- H -+------- CP(pi/2) -------+x--
                                 |          |              ||
    |q2âŸ© ------------------------*--------- * ------ H ----x+--
                                                           |
                                                        SWAP

Gate Count Formula:
  - Hadamard gates:          n
  - Controlled phases:       n(n-1)/2
  - SWAP gates:              floor(n/2)
  - Total:                   n + n(n-1)/2 + floor(n/2) ~ n^2/2
*)

// ===========================================================================
// EXAMPLE: Gate Count Calculation
// ===========================================================================

if shouldRun "gates" then
    if not quiet then
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
        printfn ""
        printfn "    Breakdown:"
        printfn "      After H on q0: CP from q1, q2, q3 = 3 gates"
        printfn "      After H on q1: CP from q2, q3 = 2 gates"
        printfn "      After H on q2: CP from q3 = 1 gate"
        printfn "      Total: 3 + 2 + 1 = 6 gates"
        printfn ""
        printfn "  Step 3: Count SWAP gates (for bit reversal)"
        printfn "    SWAP gates = floor(4/2) = 2"
        printfn "    (Swap q0<->q3 and q1<->q2)"
        printfn ""
        printfn "  ANSWER: Total gates = 4 + 6 + 2 = 12 gates"
        printfn ""

    let gateCount4 = estimateGateCount 4 true
    if not quiet then
        printfn "  Library verification: estimateGateCount 4 true = %d gates" gateCount4
        printfn ""
        printfn "  COMPARISON WITH CLASSICAL:"
        printfn "    Classical DFT:  N^2 = (2^4)^2 = 256 operations"
        printfn "    Classical FFT:  N log2 N = 16 x 4 = 64 operations"
        printfn "    Quantum QFT:    n^2/2 + 3n/2 = 12 gates"
        printfn ""

    results.Add(
        [ "example", "gates"
          "qubits", "4"
          "gate_count", string gateCount4
          "detail", "Gate count calculation for 4 qubits" ]
        |> Map.ofList)

(*
===============================================================================
 Chapter 3: QFT on Computational Basis States - Worked Examples
===============================================================================

When QFT is applied to a computational basis state |jâŸ©, the result is an
equal superposition of all basis states with specific phase relationships:

    QFT|jâŸ© = (1/âˆšN) Î£â‚– e^{2Ï€ijk/N} |kâŸ©

Key insight: ALL output amplitudes have EQUAL magnitude (1/âˆšN), but different
PHASES determined by the input j.
*)

// ===========================================================================
// EXAMPLE: QFT on |0âŸ©
// ===========================================================================

if shouldRun "qft0" then
    let n = numQubits
    let nStates = 1 <<< n

    if not quiet then
        printfn "============================================================================="
        printfn " EXAMPLE 3.1: QFT on |0> (%d qubits)" n
        printfn "============================================================================="
        printfn ""
        printfn "PROBLEM: Apply QFT to |0> = |%s> with N = 2^%d = %d." (String.replicate n "0") n nStates
        printfn ""
        printfn "SOLUTION:"
        printfn "  For j = 0:"
        printfn "    QFT|0> = (1/sqrt(%d)) Sum_k e^{2pi i * 0 * k/%d} |k>" nStates nStates
        printfn "           = (1/sqrt(%d)) Sum_k |k>  (all phases are 0)" nStates
        printfn ""
        printfn "  This is an EQUAL SUPERPOSITION with no phase differences!"
        printfn ""
        let amp = 1.0 / sqrt (float nStates)
        printfn "  Output amplitudes: 1/sqrt(%d) = %.4f for each state" nStates amp
        printfn "  Expected measurement probability: 1/%d = %.1f%% for each basis state" nStates (100.0 / float nStates)
        printfn ""

    match execute n backend defaultConfig with
    | Ok result ->
        let counts = measureCounts result.FinalState nStates 1000
        if not quiet then
            printfn "  LIBRARY VERIFICATION:"
            printfn "    Measurement results (1000 shots):"
            for k in 0 .. nStates - 1 do
                let pct = float counts.[k] / 10.0
                printfn "      |%d>: %3d shots (%.1f%%)" k counts.[k] pct
            printfn ""
            printfn "    Gate count: %d" result.GateCount
            printfn "    Execution time: %.2f ms" result.ExecutionTimeMs
            printfn ""

        results.Add(
            [ "example", "qft0"
              "qubits", string n
              "gate_count", string result.GateCount
              "execution_time_ms", sprintf "%.2f" result.ExecutionTimeMs
              "detail", sprintf "QFT on |0> with %d qubits" n ]
            |> Map.ofList)
    | Error err ->
        if not quiet then printfn "  Error: %A" err

// ===========================================================================
// EXAMPLE: QFT on |1âŸ©
// ===========================================================================

if shouldRun "qft1" then
    let n = numQubits
    let nStates = 1 <<< n

    if not quiet then
        printfn "============================================================================="
        printfn " EXAMPLE 3.2: QFT on |1> (%d qubits)" n
        printfn "============================================================================="
        printfn ""
        printfn "PROBLEM: Apply QFT to |1> with N = %d." nStates
        printfn ""
        printfn "SOLUTION:"
        printfn "  For j = 1:"
        printfn "    QFT|1> = (1/sqrt(%d)) Sum_k e^{2pi i * k/%d} |k>" nStates nStates
        printfn ""
        printfn "  Output amplitudes (equal magnitude, varying phase):"

    let omega = Complex.Exp(Complex(0.0, 2.0 * Math.PI / float (1 <<< numQubits)))
    if not quiet then
        for k in 0 .. (1 <<< numQubits) - 1 do
            let phase_k = omega ** float k
            let angleDegrees = phase_k.Phase * 180.0 / Math.PI
            printfn "    |%d>: phase = %.0f degrees" k angleDegrees
        printfn ""
        printfn "  Key observation: Amplitudes form a spiral in the complex plane!"
        printfn ""

    match transformBasisState n 1 backend defaultConfig with
    | Ok result ->
        let counts = measureCounts result.FinalState nStates 1000
        if not quiet then
            printfn "  LIBRARY VERIFICATION:"
            printfn "    Measurement results (1000 shots):"
            for k in 0 .. nStates - 1 do
                let pct = float counts.[k] / 10.0
                printfn "      |%d>: %3d shots (%.1f%%)" k counts.[k] pct
            printfn ""
            printfn "    Note: Equal probabilities despite different phases!"
            printfn ""

        results.Add(
            [ "example", "qft1"
              "qubits", string n
              "gate_count", string result.GateCount
              "detail", sprintf "QFT on |1> with %d qubits" n ]
            |> Map.ofList)
    | Error err ->
        if not quiet then printfn "  Error: %A" err

// ===========================================================================
// EXAMPLE: QFT on |5âŸ©
// ===========================================================================

if shouldRun "qft5" then
    let n = numQubits
    let nStates = 1 <<< n

    if nStates <= 5 then
        if not quiet then
            printfn "  Skipping QFT|5> example: requires at least 3 qubits (N >= 8)."
            printfn ""
    else
        if not quiet then
            printfn "============================================================================="
            printfn " EXAMPLE 3.3: QFT on |5> (%d qubits)" n
            printfn "============================================================================="
            printfn ""
            printfn "PROBLEM: Apply QFT to |5> = |101> (binary) with N = %d." nStates
            printfn ""
            printfn "SOLUTION:"
            printfn "  For j = 5:"
            printfn "    QFT|5> = (1/sqrt(%d)) Sum_k e^{2pi i * 5 * k/%d} |k>" nStates nStates
            printfn ""
            printfn "  Phase calculation for each output state:"
            for k in 0 .. nStates - 1 do
                let totalPhase = (2.0 * Math.PI * 5.0 * float k / float nStates) % (2.0 * Math.PI)
                let angleDeg =
                    let a = if totalPhase > Math.PI then totalPhase - 2.0 * Math.PI else totalPhase
                    a * 180.0 / Math.PI
                printfn "    |%d>: phase = %.0f degrees" k angleDeg
            printfn ""

        match transformBasisState n 5 backend defaultConfig with
        | Ok result ->
            if not quiet then
                printfn "  LIBRARY VERIFICATION:"
                printfn "    QFT transformation successful!"
                printfn "    Gate count: %d" result.GateCount
                printfn "    State is normalized: %b" (QuantumState.isNormalized result.FinalState)
                printfn ""

            results.Add(
                [ "example", "qft5"
                  "qubits", string n
                  "gate_count", string result.GateCount
                  "detail", sprintf "QFT on |5> with %d qubits" n ]
                |> Map.ofList)
        | Error err ->
            if not quiet then printfn "  Error: %A" err

(*
===============================================================================
 Chapter 4: The QFT-Inverse QFT Identity (Unitarity)
===============================================================================

Since QFT is unitary, applying QFT followed by its inverse (QFTâ€ ) returns
the original state:

    QFTâ€  Â· QFT = I (identity)

This is analogous to the classical Fourier transform pair where transforming
to frequency domain and back recovers the original signal.
*)

// ===========================================================================
// EXAMPLE: Round-Trip Verification
// ===========================================================================

if shouldRun "roundtrip" then
    let n = numQubits

    if not quiet then
        printfn "============================================================================="
        printfn " EXAMPLE 4.1: Round-Trip Verification (QFT -> QFTâ€  = Identity)"
        printfn "============================================================================="
        printfn ""
        printfn "PROBLEM: Verify that QFT followed by inverse QFT returns |0> to |0>."
        printfn ""

    match verifyRoundTrip n backend with
    | Ok isIdentity ->
        if not quiet then
            if isIdentity then
                printfn "  Result: PASSED"
                printfn "  All measurements returned |%s>" (String.replicate n "0")
                printfn "  QFT * QFTâ€  = Identity verified!"
            else
                printfn "  Result: FAILED"
                printfn "  Some measurements were not |%s>" (String.replicate n "0")
            printfn ""

        results.Add(
            [ "example", "roundtrip"
              "qubits", string n
              "passed", string isIdentity
              "detail", "QFT round-trip identity verification" ]
            |> Map.ofList)
    | Error err ->
        if not quiet then printfn "  Error: %A" err

    if not quiet then
        printfn "  UNITARITY VERIFICATION (with config options):"
    for applySwaps in [true; false] do
        for inverse in [true; false] do
            let config = { defaultConfig with ApplySwaps = applySwaps; Inverse = inverse }
            let desc = sprintf "ApplySwaps=%b, Inverse=%b" applySwaps inverse
            match verifyUnitarity n backend config with
            | Ok isUnitary ->
                let status = if isUnitary then "PASS" else "FAIL"
                if not quiet then printfn "    %s: %s" desc status
            | Error err ->
                if not quiet then printfn "    %s: ERROR - %A" desc err
    if not quiet then printfn ""

(*
===============================================================================
 Chapter 5: Applications of QFT
===============================================================================
*)

// ===========================================================================
// EXAMPLE: Complexity Comparison
// ===========================================================================

if shouldRun "complexity" then
    if not quiet then
        printfn "============================================================================="
        printfn " EXAMPLE 5.1: Complexity Comparison - Classical vs Quantum"
        printfn "============================================================================="
        printfn ""
        printfn "  Problem: Transform a signal/state of size N = 2^n"
        printfn ""
        printfn "  | n qubits |    N states   | Classical DFT | Classical FFT | Quantum QFT |"
        printfn "  |----------|---------------|---------------|---------------|-------------|"

    for n in [4; 8; 16; 20; 30] do
        let N = 1L <<< n
        let dft = if n <= 16 then (N * N).ToString("N0") else sprintf "~10^%d" (2 * n / 3)
        let fft = if n <= 30 then (N * int64 n).ToString("N0") else sprintf "~10^%d" (n / 3)
        let qft = estimateGateCount n true
        let nStr = if n <= 20 then N.ToString("N0") else sprintf "~10^%d" (n / 3)
        if not quiet then
            printfn "  |    %2d    | %13s | %13s | %13s | %11d |" n nStr dft fft qft

        results.Add(
            [ "example", "complexity"
              "qubits", string n
              "gate_count", string qft
              "detail", sprintf "Complexity comparison for n=%d" n ]
            |> Map.ofList)

    if not quiet then
        printfn ""
        printfn "  At n=50 qubits: QFT uses ~2,500 gates on ~10^15 amplitudes!"
        printfn "  Classical computers cannot even STORE that many numbers."
        printfn ""

    if not quiet then
        printfn "============================================================================="
        printfn " EXAMPLE 5.2: QFT in Shor's Algorithm Context"
        printfn "============================================================================="
        printfn ""
        printfn "PROBLEM: Factor N=15 using Shor's algorithm (simplified overview)."
        printfn ""
        printfn "SOLUTION OUTLINE:"
        printfn "  Step 1: Choose random a = 7 (coprime with 15)"
        printfn "  Step 2: Find the period r of f(x) = 7^x mod 15"
        printfn "          f(0)=1, f(1)=7, f(2)=4, f(3)=13, f(4)=1, ..."
        printfn "          Period r = 4"
        printfn "  Step 3: (QUANTUM) Use QFT to find period via interference"
        printfn "  Step 4: Extract factors: gcd(7^2 + 1, 15) = 5, gcd(7^2 - 1, 15) = 3"
        printfn "  ANSWER: 15 = 3 x 5"
        printfn ""

// ===========================================================================
// EXERCISES
// ===========================================================================

if shouldRun "exercises" then
    if not quiet then
        printfn "============================================================================="
        printfn " EXERCISES"
        printfn "============================================================================="
        printfn ""
        printfn "Exercise 1 (Basic): Gate Count Calculation"
        printfn "-------------------------------------------"
        printfn "Calculate the total gate count for QFT on 5 qubits."
        printfn ""

    let answer1 = estimateGateCount 5 true
    if not quiet then
        printfn "  Library answer: %d gates" answer1
        printfn ""

    results.Add(
        [ "example", "exercise_1"
          "qubits", "5"
          "gate_count", string answer1
          "detail", "Gate count for 5 qubits" ]
        |> Map.ofList)

    if not quiet then
        printfn "Exercise 2 (Intermediate): Phase Calculation"
        printfn "---------------------------------------------"
        printfn "For QFT|3> with 2 qubits (N=4), calculate the phase of each output amplitude."
        printfn ""
        printfn "  SOLUTION:"
        for k in 0..3 do
            let phase = (2.0 * Math.PI * 3.0 * float k / 4.0) % (2.0 * Math.PI)
            let phasePi = phase / Math.PI
            printfn "    k=%d: phase = %.2f*pi radians = %.0f degrees" k phasePi (phase * 180.0 / Math.PI)
        printfn ""

        printfn "Exercise 3 (Advanced): Verify QFT on All 2-Qubit Basis States"
        printfn "--------------------------------------------------------------"
        printfn ""

    for j in 0..3 do
        if not quiet then printfn "  QFT|%d>:" j
        match transformBasisState 2 j backend defaultConfig with
        | Ok result ->
            let counts = measureCounts result.FinalState 4 400
            if not quiet then
                printfn "    Measurements: |0>=%d, |1>=%d, |2>=%d, |3>=%d"
                    counts.[0] counts.[1] counts.[2] counts.[3]
                printfn "    Expected: ~100 each (25%%)"
        | Error err ->
            if not quiet then printfn "    Error: %A" err

    if not quiet then printfn ""

// ---------------------------------------------------------------------------
// Summary
// ---------------------------------------------------------------------------

if not quiet then
    printfn "============================================================================="
    printfn " SUMMARY: Key Takeaways"
    printfn "============================================================================="
    printfn ""
    printfn "  1. QFT transforms computational basis to frequency basis"
    printfn "     QFT|j> = (1/sqrt(N)) Sum_k e^{2pi i jk/N} |k>"
    printfn ""
    printfn "  2. Gate complexity: O(n^2) for n qubits"
    printfn ""
    printfn "  3. Exponential speedup over classical DFT/FFT"
    printfn ""
    printfn "  4. QFT is UNITARY: QFT * QFTâ€  = Identity"
    printfn ""
    printfn "  5. Applications: Shor's algorithm, QPE, period finding"
    printfn ""
    printfn "============================================================================="

// ---------------------------------------------------------------------------
// Structured output
// ---------------------------------------------------------------------------

let resultsList = results |> Seq.toList

match outputPath with
| Some p -> Reporting.writeJson p resultsList
| None -> ()

match csvPath with
| Some p ->
    let header = ["example"; "qubits"; "gate_count"; "execution_time_ms"; "passed"; "detail"]
    let rows =
        resultsList
        |> List.map (fun m ->
            header |> List.map (fun h -> m |> Map.tryFind h |> Option.defaultValue ""))
    Reporting.writeCsv p header rows
| None -> ()

if argv.Length = 0 then
    printfn ""
    printfn "Tip: run with --help for CLI options."
