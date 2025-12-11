# Quantum Random Number Generator (QRNG) API

## Overview

The Quantum Random Number Generator (QRNG) provides **true random numbers** using quantum measurement, as opposed to pseudo-random classical algorithms. Quantum measurements are fundamentally non-deterministic, providing genuine randomness suitable for cryptographic applications, Monte Carlo simulations, and scientific computing.

**Key Features:**
- True randomness from quantum measurement (not pseudo-random)
- Multiple output formats (bits, integers, floats, bytes)
- Backend integration for real quantum hardware
- Statistical quality testing

**When to Use:**
- ✅ Cryptographic key generation
- ✅ Secure token generation
- ✅ Monte Carlo simulations requiring true randomness
- ✅ Scientific simulations

**When NOT to Use (use System.Random instead):**
- ❌ Local quantum circuit simulation
- ❌ Test data generation
- ❌ Reproducible experiments (requires seeding)
- ❌ Classical algorithm randomization

---

## Quick Start

### Generate Random Bytes (e.g., Cryptographic Key)

```fsharp
open FSharp.Azure.Quantum.Algorithms

// Generate 256-bit (32-byte) cryptographic key
let keyBytes = QRNG.generateBytes 32
printfn "Key: %s" (System.Convert.ToBase64String(keyBytes))
```

### Generate Random Integer in Range

```fsharp
// Simulate 6-sided die roll
let diceRoll = QRNG.generateInt 6 + 1  // Returns 1-6
printfn "Rolled: %d" diceRoll

// Random index for array of 100 elements
let randomIndex = QRNG.generateInt 100  // Returns 0-99
```

### Generate Random Float [0.0, 1.0)

```fsharp
// Monte Carlo sampling
let randomSample = QRNG.generateFloat()  // Returns float in [0.0, 1.0)
printfn "Sample: %.6f" randomSample
```

---

## Core Functions

### `generate`
```fsharp
val generate : numBits:int -> QRNGResult
```

Generates random bits using quantum measurement (Hadamard + measurement).

**Parameters:**
- `numBits` - Number of random bits to generate (1 to 1,000,000)

**Returns:** `QRNGResult` containing:
- `Bits: bool[]` - Array of random bits
- `AsInteger: uint64 option` - Integer representation (if ≤64 bits)
- `AsBytes: byte[]` - Byte array representation
- `Entropy: float` - Shannon entropy (0.0-1.0, should be ~1.0)

**Example:**
```fsharp
let result = QRNG.generate 8
printfn "Bits: %A" result.Bits
printfn "As byte: %d" result.AsBytes.[0]
printfn "Entropy: %.3f" result.Entropy
```

---

### `generateInt`
```fsharp
val generateInt : maxValue:int -> int
```

Generates random integer in range `[0, maxValue)` using rejection sampling.

**Parameters:**
- `maxValue` - Upper bound (exclusive), must be positive

**Returns:** Random integer `n` where `0 ≤ n < maxValue`

**Example:**
```fsharp
// Random number from 0-99
let randomPercent = QRNG.generateInt 100

// Random array index
let arr = [|1; 2; 3; 4; 5|]
let idx = QRNG.generateInt arr.Length
let randomElement = arr.[idx]
```

---

### `generateFloat`
```fsharp
val generateFloat : unit -> float
```

Generates random float in range `[0.0, 1.0)` with 53-bit precision (IEEE 754 double mantissa).

**Returns:** Random `float` in `[0.0, 1.0)`

**Example:**
```fsharp
// Monte Carlo estimation of π
let estimatePi samples =
    [1..samples]
    |> List.map (fun _ ->
        let x = QRNG.generateFloat()
        let y = QRNG.generateFloat()
        if x*x + y*y <= 1.0 then 1 else 0)
    |> List.average
    |> (*) 4.0

let pi = estimatePi 10000
printfn "π ≈ %.4f" pi
```

---

### `generateBytes`
```fsharp
val generateBytes : numBytes:int -> byte[]
```

Generates random byte array (8 bits per byte).

**Parameters:**
- `numBytes` - Number of bytes to generate (must be positive)

**Returns:** `byte[]` with random values

**Example:**
```fsharp
// Generate 256-bit AES key
let aesKey = QRNG.generateBytes 32

// Generate random salt for password hashing
let salt = QRNG.generateBytes 16
```

---

## Backend Integration

### `generateWithBackend`
```fsharp
val generateWithBackend : 
    numBits:int -> 
    backend:IQuantumBackend -> 
    Async<Result<QRNGResult, string>>
```

Generates random bits using a **real quantum hardware backend** (IonQ, Rigetti, etc.).

**⚠️ Cost Warning:** Most real quantum backends charge per circuit execution. For production QRNG, `LocalBackend` is recommended unless you specifically need hardware-generated randomness for cryptographic certification.

**Parameters:**
- `numBits` - Number of bits (max 1000 for single execution)
- `backend` - Quantum backend instance

**Returns:** `Async<Result<QRNGResult, string>>`

**Example:**
```fsharp
open FSharp.Azure.Quantum.Backends

async {
    // Use local simulator (free, fast)
    let backend = LocalBackendFactory.createUnified()
    
    let! result = QRNG.generateWithBackend 32 backend
    
    match result with
    | Ok qrng -> 
        printfn "Generated 32 bits with entropy: %.3f" qrng.Entropy
    | Error err -> 
        printfn "Error: %s" err.Message
}
|> Async.RunSynchronously
```

---

## Statistical Quality Testing

### `testRandomness`
```fsharp
val testRandomness : bits:bool[] -> RandomnessTest
```

Performs basic statistical tests on generated bits to assess randomness quality.

**Tests Performed:**
1. **Frequency Test**: Ratio of 1s vs 0s (should be ~0.5)
2. **Run Test**: Count of alternations between 0 and 1
3. **Entropy**: Shannon entropy (should be ~1.0 for perfect randomness)

**Returns:** `RandomnessTest` with:
- `FrequencyRatio: float` - Ratio of 1s (should be ~0.5)
- `RunCount: int` - Number of bit alternations
- `Entropy: float` - Shannon entropy (0.0-1.0)
- `Quality: string` - Assessment: "EXCELLENT", "GOOD", "ACCEPTABLE", or "POOR"

**Example:**
```fsharp
let result = QRNG.generate 10000
let test = QRNG.testRandomness result.Bits

printfn "Frequency Ratio: %.3f (expect ~0.500)" test.FrequencyRatio
printfn "Entropy: %.3f (expect ~1.000)" test.Entropy
printfn "Quality: %s" test.Quality
```

**Expected Output:**
```
Frequency Ratio: 0.498 (expect ~0.500)
Entropy: 0.997 (expect ~1.000)
Quality: EXCELLENT
```

---

## Technical Details

### Algorithm

QRNG uses the simplest quantum circuit for true randomness:

1. **Initialize** qubits to |0⟩ state
2. **Apply Hadamard gate** to each qubit → Creates uniform superposition: |ψ⟩ = (|0⟩ + |1⟩)/√2
3. **Measure** in computational basis → Each qubit collapses to 0 or 1 with exactly 50% probability
4. **Extract bits** from measurement outcomes

**Quantum Advantage:** Unlike pseudo-random number generators (PRNGs) which are deterministic and periodic, quantum measurements provide **genuine randomness** due to the fundamental indeterminism of quantum mechanics.

### Entropy Calculation

Shannon entropy for binary sequence:

```
H = -p₀ log₂(p₀) - p₁ log₂(p₁)
```

Where:
- `p₀` = probability of 0 (count of 0s / total bits)
- `p₁` = probability of 1 (count of 1s / total bits)

Perfect randomness: `H = 1.0` (maximum entropy for binary)

### Batch Processing

To avoid excessive memory usage, QRNG processes bits in batches of ≤20 qubits at a time (state vector size = 2²⁰ = 1M complex numbers). This allows generating up to 1 million random bits efficiently.

---

## References

- **Hidary, J.D.** (2021). *Quantum Computing: An Applied Approach*, 2nd ed., Chapter 9.7: Quantum Random Number Generator
- **Lloyd, S.** (1993). "Ultimate physical limits to computation." *Nature*, 406(6799), 1047-1054.
- **NIST SP 800-90B** (2018). Recommendation for the Entropy Sources Used for Random Bit Generation

---

## See Also

- [Mathematical Foundations](Mathematical-Foundations.md) - Quantum measurement theory
- [Hardware Selection Guide](Hardware-Selection-Guide.md) - Choosing quantum backends
- [Backend Abstraction API](backend-switching.md) - Working with quantum backends
