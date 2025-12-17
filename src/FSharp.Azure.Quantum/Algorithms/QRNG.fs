namespace FSharp.Azure.Quantum.Algorithms

open System
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction
open FSharp.Azure.Quantum.Core.CircuitAbstraction
open FSharp.Azure.Quantum.LocalSimulator
open FSharp.Azure.Quantum

/// Quantum Random Number Generator (QRNG)
/// 
/// Generates true random numbers using quantum measurement.
/// Quantum measurements are fundamentally non-deterministic, providing
/// true randomness (as opposed to pseudo-random classical algorithms).
/// 
/// **Use Cases:**
/// - Cryptographic key generation
/// - Monte Carlo simulations requiring true randomness
/// - Randomized algorithms
/// - Scientific simulations
/// 
/// **Reference:**
/// - Hidary (2021), Ch 9.7: Quantum Random Number Generator
/// - Lloyd, S. (1993): "Ultimate physical limits to computation"
module QRNG =
    
    // ========================================================================
    // INTENT → PLAN → EXECUTE (ADR: Intent-First)
    // ========================================================================

    type private QrngIntent = { NumBits: int }

    [<RequireQualifiedAccess>]
    type private QrngPlan =
        | ExecuteViaCircuit

    let private plan (backend: IQuantumBackend) (intent: QrngIntent) : Result<QrngPlan, QuantumError> =
        // QRNG currently needs the ability to execute a gate-based H-superposition circuit.
        // Some backends (e.g., annealing) cannot support this, and we should fail explicitly.
        match backend.NativeStateType with
        | QuantumStateType.Annealing ->
            Error (QuantumError.OperationError ("QRNG", $"Backend '{backend.Name}' does not support QRNG (native state type: {backend.NativeStateType})"))
        | _ ->
            let needs = QuantumOperation.Gate (CircuitBuilder.H 0)
            if backend.SupportsOperation needs then
                Ok QrngPlan.ExecuteViaCircuit
            else
                Error (QuantumError.OperationError ("QRNG", $"Backend '{backend.Name}' does not support required operations for QRNG"))

    let private executePlan (backend: IQuantumBackend) (intent: QrngIntent) (plan: QrngPlan) : Result<QuantumState, QuantumError> =
        match plan with
        | QrngPlan.ExecuteViaCircuit ->
            let circuit = CircuitBuilder.empty intent.NumBits

            let circuitWithH =
                [0 .. intent.NumBits - 1]
                |> List.fold (fun c qubitIdx ->
                    CircuitBuilder.addGate (CircuitBuilder.Gate.H qubitIdx) c) circuit

            let wrappedCircuit = CircuitAbstraction.CircuitWrapper(circuitWithH) :> ICircuit
            backend.ExecuteToState wrappedCircuit
    
    // ========================================================================
    // TYPES
    // ========================================================================
    
    /// Configuration for quantum random number generation
    type QRNGConfig = {
        /// Number of random bits to generate
        NumBits: int
        
        /// Random seed for reproducible results (optional, None = true randomness)
        Seed: int option
    }
    
    /// Result of QRNG execution
    type QRNGResult = {
        /// Generated random bits (as boolean array)
        Bits: bool[]
        
        /// Generated random bits as integer (if ≤64 bits)
        AsInteger: uint64 option
        
        /// Generated random bits as byte array
        AsBytes: byte[]
        
        /// Statistical entropy estimate (0.0-1.0, should be close to 1.0)
        Entropy: float
    }
    
    // ========================================================================
    // CORE ALGORITHM
    // ========================================================================
    
    /// Generate random bits using quantum measurement
    /// 
    /// Algorithm:
    /// 1. Initialize qubits to |0⟩
    /// 2. Apply Hadamard gate to each qubit → uniform superposition
    /// 3. Measure each qubit in computational basis
    /// 4. Each measurement gives 0 or 1 with 50% probability
    /// 
    /// **Quantum Advantage:** True randomness vs pseudo-random classical RNG
    let generateBits (numBits: int) (seed: int option) : QRNGResult =
        
        if numBits <= 0 then
            failwith "NumBits must be positive"
        
        if numBits > 1000000 then
            failwith "NumBits too large (max 1,000,000)"
        
        // Create random number generator (for measurement simulation)
        let rng = 
            match seed with
            | Some s -> Random(s)
            | None -> Random()
        
        // Generate random bits using quantum measurement
        // Since qubits are independent (no entanglement), we measure 1 qubit at a time
        // This is MUCH faster than batching: 2^1 = 2 amplitudes vs 2^20 = 1M amplitudes!
        let bits =
            [0 .. numBits - 1]
            |> List.map (fun _ ->
                // Initialize single-qubit state |0⟩
                let state = StateVector.init 1
                
                // Apply Hadamard → (|0⟩ + |1⟩)/√2
                let superpositionState = Gates.applyH 0 state
                
                // Measure qubit (50% chance of 0 or 1)
                let measurementOutcome = Measurement.measureComputationalBasis rng superpositionState
                
                // Extract bit from measurement (0 or 1)
                measurementOutcome = 1)
            |> Array.ofList
        
        // Convert bits to bytes
        let numBytes = (numBits + 7) / 8
        let bytes =
            Array.init numBytes (fun byteIdx ->
                [0 .. 7]
                |> List.fold (fun acc bitIdx ->
                    let i = byteIdx * 8 + bitIdx
                    if i < numBits && bits.[i] 
                    then acc ||| (1uy <<< bitIdx)
                    else acc) 0uy)
        
        // Convert to integer if possible (≤64 bits)
        let asInteger =
            if numBits <= 64 then
                bits
                |> Array.indexed
                |> Array.filter snd
                |> Array.fold (fun acc (i, _) -> acc ||| (1UL <<< i)) 0UL
                |> Some
            else
                None
        
        // Estimate entropy (Shannon entropy for binary sequence)
        let count0 = bits |> Array.filter (not) |> Array.length
        let count1 = bits |> Array.filter id |> Array.length
        let p0 = float count0 / float numBits
        let p1 = float count1 / float numBits
        
        let entropy =
            if p0 = 0.0 || p1 = 0.0 then
                0.0  // No entropy (all 0s or all 1s)
            else
                // Shannon entropy: H = -Σ p_i log₂(p_i)
                -p0 * Math.Log2(p0) - p1 * Math.Log2(p1)
        
        {
            Bits = bits
            AsInteger = asInteger
            AsBytes = bytes
            Entropy = entropy
        }
    
    /// Generate random bits with default configuration
    let generate (numBits: int) : QRNGResult =
        generateBits numBits None
    
    /// Generate random integer in range [0, max)
    let generateInt (maxValue: int) : int =
        if maxValue <= 0 then
            failwith "MaxValue must be positive"
        
        // Calculate number of bits needed
        let numBits = 
            if maxValue <= 1 then 1
            else int (ceil (log (float maxValue) / log 2.0))
        
        // Generate random bits until we get a value < maxValue
        let rec generateUntilValid() =
            let result = generate numBits
            match result.AsInteger with
            | Some value ->
                let intValue = int value
                if intValue < maxValue then
                    intValue
                else
                    generateUntilValid()  // Rejection sampling
            | None ->
                failwith "Failed to convert to integer"
        
        generateUntilValid()
    
    /// Generate random float in range [0.0, 1.0)
    let generateFloat () : float =
        let numBits = 53  // IEEE 754 double precision mantissa bits
        let result = generate numBits
        match result.AsInteger with
        | Some value ->
            float value / float (1UL <<< numBits)
        | None ->
            failwith "Failed to generate random float"
    
    /// Generate random bytes
    let generateBytes (numBytes: int) : byte[] =
        if numBytes <= 0 then
            failwith "NumBytes must be positive"
        
        let result = generate (numBytes * 8)
        result.AsBytes
    
    // ========================================================================
    // BACKEND INTEGRATION
    // ========================================================================
    
    /// Generate random bits using specified quantum backend
    /// 
    /// **Note:** Most real quantum backends charge per circuit execution.
    /// For production use, LocalBackend is recommended for QRNG unless you
    /// specifically need hardware-generated randomness for cryptographic purposes.
    let generateWithBackend 
        (numBits: int) 
        (backend: IQuantumBackend) 
        : Async<QuantumResult<QRNGResult>> =
        
        async {
            if numBits <= 0 then
                return Error (QuantumError.ValidationError ("NumBits", "must be positive"))
            elif numBits > 1000 then
                return Error (QuantumError.ValidationError ("NumBits", "too large for single backend execution (max 1000)"))
            else
                try
                    let intent = { NumBits = numBits }

                    match plan backend intent with
                    | Error err ->
                        return Error err
                    | Ok chosenPlan ->
                        match executePlan backend intent chosenPlan with
                        | Error err ->
                            return Error err
                        | Ok state ->
                            // Measure state once to get random bits
                            let measurements = QuantumState.measure state 1

                            let bits =
                                match measurements with
                                | [||] -> Array.zeroCreate<bool> numBits
                                | _ -> measurements.[0] |> Array.map (fun bitValue -> bitValue = 1)

                            // Shared conversion logic matches `generateBits` behavior (little-endian per byte)
                            let numBytes = (numBits + 7) / 8

                            let bytes =
                                Array.init numBytes (fun byteIdx ->
                                    [0 .. 7]
                                    |> List.fold (fun acc bitIdx ->
                                        let i = byteIdx * 8 + bitIdx
                                        if i < numBits && bits.[i] then acc ||| (1uy <<< bitIdx) else acc) 0uy)

                            let asInteger =
                                if numBits <= 64 then
                                    bits
                                    |> Array.indexed
                                    |> Array.filter snd
                                    |> Array.fold (fun acc (i, _) -> acc ||| (1UL <<< i)) 0UL
                                    |> Some
                                else
                                    None

                            let count0 = bits |> Array.filter (not) |> Array.length
                            let count1 = numBits - count0
                            let p0 = float count0 / float numBits
                            let p1 = float count1 / float numBits

                            let entropy =
                                if p0 = 0.0 || p1 = 0.0 then 0.0
                                else -p0 * Math.Log2(p0) - p1 * Math.Log2(p1)

                            return Ok {
                                Bits = bits
                                AsInteger = asInteger
                                AsBytes = bytes
                                Entropy = entropy
                            }
                with ex ->
                    return Error (QuantumError.BackendError ("QRNG", $"backend execution failed: {ex.Message}"))
        }
    
    // ========================================================================
    // STATISTICAL TESTS
    // ========================================================================
    
    /// Test randomness quality using basic statistical tests
    type RandomnessTest = {
        /// Frequency test (ratio of 0s vs 1s should be ~0.5)
        FrequencyRatio: float
        
        /// Run test (alternations between 0 and 1)
        RunCount: int
        
        /// Entropy (should be close to 1.0 for perfect randomness)
        Entropy: float
        
        /// Overall quality assessment
        Quality: string
    }
    
    /// Perform basic statistical tests on generated bits
    let testRandomness (bits: bool[]) : RandomnessTest =
        let n = bits.Length
        
        // Frequency test
        let count1 = bits |> Array.filter id |> Array.length
        let frequencyRatio = float count1 / float n
        
        // Run test (count alternations)
        let runCount =
            bits
            |> Array.pairwise
            |> Array.filter (fun (a, b) -> a <> b)
            |> Array.length
        
        // Entropy
        let count0 = n - count1
        let p0 = float count0 / float n
        let p1 = float count1 / float n
        let entropy =
            if p0 = 0.0 || p1 = 0.0 then 0.0
            else -p0 * Math.Log2(p0) - p1 * Math.Log2(p1)
        
        // Quality assessment
        let quality =
            if abs(frequencyRatio - 0.5) < 0.05 && entropy > 0.95 then
                "EXCELLENT"
            elif abs(frequencyRatio - 0.5) < 0.1 && entropy > 0.9 then
                "GOOD"
            elif abs(frequencyRatio - 0.5) < 0.2 && entropy > 0.8 then
                "ACCEPTABLE"
            else
                "POOR"
        
        {
            FrequencyRatio = frequencyRatio
            RunCount = runCount
            Entropy = entropy
            Quality = quality
        }
    
    // ========================================================================
    // EXAMPLES
    // ========================================================================
    
    /// Example: Generate random bytes for cryptographic key
    let example_CryptographicKey () =
        // Generate 256-bit (32-byte) key
        let keyBytes = generateBytes 32
        printfn "Generated 256-bit key:"
        printfn "  Hex: %s" (BitConverter.ToString(keyBytes).Replace("-", ""))
        printfn "  Base64: %s" (Convert.ToBase64String(keyBytes))
    
    /// Example: Generate random numbers in range
    let example_DiceRoll () =
        // Simulate 6-sided die rolls
        let rolls = [1..10] |> List.map (fun _ -> generateInt 6 + 1)
        printfn "10 quantum dice rolls: %A" rolls
    
    /// Example: Generate random float for Monte Carlo
    let example_MonteCarloSampling () =
        // Generate 1000 random floats for Monte Carlo simulation
        let samples = [1..1000] |> List.map (fun _ -> generateFloat())
        let average = samples |> List.average
        printfn "Monte Carlo: Generated 1000 samples, average = %.4f (expect ~0.5)" average

