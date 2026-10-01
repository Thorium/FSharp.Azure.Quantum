# Quantum Random Number Generator (QRNG) API

## Overview

The Quantum Random Number Generator (QRNG) models random number generation via quantum measurement. On real quantum hardware, measurements are fundamentally non-deterministic and yield true (quantum) randomness.

> **⚠️ Important — local simulation is not quantum randomness.** By default this module simulates the measurements classically. The unseeded local path draws its outcomes from the OS cryptographically secure RNG (`System.Security.Cryptography.RandomNumberGenerator`) — CSPRNG-quality, suitable for cryptographic key material, but still classical randomness. True quantum randomness comes from `generateWithBackendAsync` on a cloud QPU target created with `shots = 1`: the bits are then the one measured shot of that job.

**Key Features:**
- Quantum-measurement model of randomness (local simulation is CSPRNG-backed, not quantum)
- Multiple output formats (bits, integers, floats, bytes)
- Backend integration: run the H-superposition circuit through any gate-based `IQuantumBackend`; on a cloud QPU the bits are one measured hardware shot
- Statistical quality testing

**When to Use:**
- ✅ Cryptographic key generation (unseeded local path is CSPRNG-backed)
- ✅ Secure token generation
- ✅ Monte Carlo sampling
- ✅ Scientific simulations

**When NOT to Use (use System.Random instead):**
- ❌ Local quantum circuit simulation
- ❌ Test data generation
- ❌ Classical algorithm randomization

For reproducible experiments, pass a seed to `generateBits` (see below); the seeded path uses `System.Random(seed)` and is not suitable for cryptographic use.

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

Generates random bits from the simulated Hadamard + measurement model. Same as `generateBits numBits None`, so the bits come from the OS CSPRNG.

**Parameters:**
- `numBits` - Number of random bits to generate (1 to 1,000,000; other values throw)

**Returns:** `QRNGResult` containing:
- `Bits: bool[]` - Array of random bits
- `AsInteger: uint64 option` - Integer representation (if ≤64 bits)
- `AsBytes: byte[]` - Byte array representation
- `Entropy: float` - Shannon entropy of this sample's 0/1 frequencies (0.0-1.0, should be ~1.0); a bias measure, not a certification of randomness

**Example:**
```fsharp
let result = QRNG.generate 8
printfn "Bits: %A" result.Bits
printfn "As byte: %d" result.AsBytes.[0]
printfn "Entropy: %.3f" result.Entropy
```

---

### `generateBits`
```fsharp
val generateBits : numBits:int -> seed:int option -> QRNGResult
```

Like `generate`, with a choice of randomness source.

**Parameters:**
- `numBits` - Number of random bits to generate (1 to 1,000,000; other values throw)
- `seed` - `None` draws from the OS CSPRNG; `Some s` simulates each measurement with `System.Random(s)`, so the same seed gives the same bits (for tests, not for key material)

**Example:**
```fsharp
let a = QRNG.generateBits 16 (Some 42)
let b = QRNG.generateBits 16 (Some 42)
printfn "Reproducible: %b" (a.Bits = b.Bits)  // true
```

---

### `generateInt`
```fsharp
val generateInt : maxValue:int -> int
```

Generates random integer in range `[0, maxValue)` using rejection sampling.

**Parameters:**
- `maxValue` - Upper bound (exclusive), must be positive (otherwise throws)

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
        if x*x + y*y <= 1.0 then 1.0 else 0.0)
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
- `numBytes` - Number of bytes to generate (must be positive, at most 125,000; otherwise throws)

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

### `generateWithBackendAsync`
```fsharp
val generateWithBackendAsync : 
    numBits:int -> 
    backend:IQuantumBackend -> 
    ct:CancellationToken -> 
    Task<QuantumResult<QRNGResult>>
```

Generates random bits by executing a `numBits`-qubit H-superposition circuit through the specified backend.

**⚠️ Randomness source** depends on the backend:
- **Cloud backends** (IonQ, Rigetti, Quantinuum, Atom Computing, IQM, Braket; anything implementing `IShotSamplingBackend`) must be created with `shots = 1`. Each call is then one job of one shot, and the `numBits` bits are that measured shot, read as returned: no classical randomness is involved, and on a QPU target they are hardware quantum randomness. A backend created with more shots returns only outcome counts, from which one shot could be picked only by classical sampling, so it is an `Error`.
- **Simulators** (`LocalBackend` and the other exact backends) return a simulated state, which is sampled once locally with a classical PRNG (`QuantumState.measure`): classical pseudo-randomness, not quantum randomness. For cryptographic key material off hardware prefer `generateBits`/`generateBytes` (unseeded → OS CSPRNG).

**⚠️ Cost Warning:** On a cloud backend every call is one billed job, so n draws are n jobs. Cap them with a `JobBudget` (see [Backend Switching](backend-switching.md)).

**Parameters:**
- `numBits` - Number of bits, 1 to 1000. The whole circuit is one `numBits`-qubit register, so the backend must also hold that many qubits: on `LocalBackend` the limit is `StateVector.maxQubits` (derived from available memory, at most 30).
- `backend` - Quantum backend instance. Annealing backends and backends without an H gate are rejected with an `Error`.
- `ct` - Cancellation token for the backend job (`CancellationToken.None` when you have none).

**Returns:** `Task<QuantumResult<QRNGResult>>` (`QuantumResult<'T>` is `Result<'T, QuantumError>`). Invalid `numBits` and backend failures come back as `Error`, not exceptions.

**Example:**
```fsharp
open System.Threading
open FSharp.Azure.Quantum.Backends

task {
    // Use local simulator (free, fast)
    let backend = LocalBackendFactory.createUnified()
    
    let! result = QRNG.generateWithBackendAsync 16 backend CancellationToken.None
    
    match result with
    | Ok qrng -> 
        printfn "Generated 16 bits with entropy: %.3f" qrng.Entropy
    | Error err -> 
        printfn "Error: %s" err.Message
}
```

On hardware, create the backend with one shot per job:

```fsharp
open System.Threading
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Backends.CloudBackends

let credential = Authentication.CredentialProviders.createDefaultCredential ()
let httpClient = Authentication.createAuthenticatedClient credential
let workspaceUrl =
    "https://<location>.quantum.azure.com/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Quantum/workspaces/<ws>"

// shots = 1: the 16 bits are the one measured shot of one billed job
let qpu = CloudBackendFactory.createIonQ httpClient workspaceUrl "ionq.qpu.aria-1" 1

task {
    match! QRNG.generateWithBackendAsync 16 qpu CancellationToken.None with
    | Ok qrng -> printfn "Hardware bits: %A" qrng.Bits
    | Error err -> printfn "Error: %s" err.Message
}
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

QRNG models the simplest quantum circuit for randomness:

1. **Initialize** qubits to |0⟩ state
2. **Apply Hadamard gate** to each qubit → Creates uniform superposition: |ψ⟩ = (|0⟩ + |1⟩)/√2
3. **Measure** in computational basis → Each qubit collapses to 0 or 1 with exactly 50% probability
4. **Extract bits** from measurement outcomes

On quantum hardware the measurement outcome is physically non-deterministic, unlike a pseudo-random number generator (PRNG), which is deterministic given its seed. `generate`, `generateBits`, `generateInt`, `generateFloat` and `generateBytes` simulate the measurement locally. `generateWithBackendAsync` on a one-shot cloud backend returns the bits the hardware measured; on a simulator it samples the returned state locally (see above).

### Entropy Calculation

Shannon entropy for binary sequence:

```
H = -p₀ log₂(p₀) - p₁ log₂(p₁)
```

Where:
- `p₀` = probability of 0 (count of 0s / total bits)
- `p₁` = probability of 1 (count of 1s / total bits)

Perfect randomness: `H = 1.0` (maximum entropy for binary)

### Memory Use

The local functions never build a multi-qubit state vector. Qubits in this circuit are independent, so the seeded path simulates one single-qubit measurement per bit, and the unseeded path draws one CSPRNG bit per measurement. Memory grows linearly with `numBits`, which is why up to 1,000,000 bits per call is practical. `generateWithBackendAsync` is different: it runs one `numBits`-qubit circuit, so its cost depends on the backend (on a cloud backend, one billed job per call).

---

## References

- **Hidary, J.D.** (2021). *Quantum Computing: An Applied Approach*, 2nd ed., Chapter 9.7: Quantum Random Number Generator
- **Lloyd, S.** (2000). "Ultimate physical limits to computation." *Nature*, 406(6799), 1047-1054.
- **NIST SP 800-90B** (2018). Recommendation for the Entropy Sources Used for Random Bit Generation

---

## See Also

- [Mathematical Foundations](Mathematical-Foundations.md) - Quantum measurement theory
- [Hardware Selection Guide](Hardware-Selection-Guide.md) - Choosing quantum backends
- [Backend Abstraction API](backend-switching.md) - Working with quantum backends
