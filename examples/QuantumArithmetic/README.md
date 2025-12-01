# Quantum Arithmetic Examples

**Business Use Case:** Cryptographic operations requiring modular arithmetic (RSA, discrete logarithm, elliptic curve crypto)

---

## Overview

The `QuantumArithmeticBuilder` provides high-level APIs for performing arithmetic operations using quantum circuits based on the Quantum Fourier Transform (QFT). This is particularly useful for:

1. **Cryptography Research** - Implementing RSA encryption, discrete logarithm problems
2. **Quantum Education** - Teaching QFT-based arithmetic without circuit-level knowledge
3. **Algorithm Prototyping** - Building blocks for Shor's algorithm and other quantum algorithms

---

## Examples

### 1. RSA Encryption (Business Scenario)

**File:** `RSAEncryption.fsx`

**Scenario:** A secure messaging application needs to encrypt messages using RSA public-key cryptography.

**What It Demonstrates:**
- Modular exponentiation: `c = m^e mod n` (core RSA operation)
- Quantum circuit resource usage (qubits, gates, depth)
- How quantum arithmetic relates to real-world cryptography

**Run:**
```bash
dotnet fsi RSAEncryption.fsx
```

**Expected Output:**
```
=== RSA Encryption Demo (Toy Example) ===

RSA Modulus (n):        33
Public Exponent (e):    3
Original Message (m):   5
Encrypted Message (c):  26

CIRCUIT STATISTICS:
  Qubits Used:     8
  Gate Count:      156
  Circuit Depth:   42
```

---

## API Reference

### F# Computation Expression

```fsharp
open FSharp.Azure.Quantum.QuantumArithmeticOps

// Modular exponentiation (RSA encryption)
let operation = quantumArithmetic {
    operands 5 3          // base = 5, exponent = 3
    operation ModularExponentiate
    modulus 33            // RSA modulus
    qubits 8              // Number of qubits
}

match execute operation with
| Ok result -> 
    printfn "Result: %d" result.Value
    printfn "Qubits: %d" result.QubitsUsed
| Error msg -> 
    printfn "Error: %s" msg
```

### F# Convenience Functions

```fsharp
// Simple operations
let addOp = add 42 17 8                          // 42 + 17
let mulOp = multiply 6 7 8                       // 6 × 7

// Modular operations (for cryptography)
let modAddOp = modularAdd 15 20 25 8             // (15 + 20) mod 25
let modMulOp = modularMultiply 6 8 13 8          // (6 × 8) mod 13
let modExpOp = modularExponentiate 5 3 33 8      // 5^3 mod 33
```

### C# API

```csharp
using static FSharp.Azure.Quantum.CSharpBuilders;

// RSA encryption: m^e mod n
var encryptOp = ModularExponentiate(
    baseValue: 5,
    exponent: 3,
    modulus: 33,
    qubits: 8
);

var result = ExecuteArithmetic(encryptOp);
if (result.IsOk) {
    var value = result.ResultValue.Value;
    Console.WriteLine($"Encrypted: {value}");
}
```

---

## Supported Operations

| Operation | Formula | Use Case |
|-----------|---------|----------|
| `Add` | `a + b` | Basic addition |
| `Multiply` | `a × b` | Basic multiplication |
| `ModularAdd` | `(a + b) mod N` | Cyclic arithmetic |
| `ModularMultiply` | `(a × b) mod N` | Cryptographic operations |
| `ModularExponentiate` | `a^x mod N` | RSA encryption, Diffie-Hellman |

---

## Important Notes

### ⚠️ Current Implementation (MVP)

This builder currently uses **classical simulation** to compute arithmetic results for educational and testing purposes. Full QFT circuit execution on quantum backends is planned for future releases.

The builder **does accept** `IQuantumBackend` parameters (Rule 1 compliant) but does not yet execute actual quantum circuits.

For production quantum arithmetic circuits, use the `QuantumArithmetic` module directly.

### 📊 Qubit Requirements

- **Toy RSA (n = 33)**: 8 qubits
- **Small RSA (n = 143)**: 10 qubits
- **Real RSA (2048-bit)**: ~4096 qubits (not feasible on current NISQ hardware)

### 🎓 Educational Value

This builder is ideal for:
- Teaching quantum arithmetic concepts
- Understanding QFT-based algorithms
- Prototyping quantum cryptographic algorithms
- Demonstrating quantum threat to RSA

**Not for:**
- Breaking real-world RSA encryption (requires fault-tolerant quantum computers)
- Production cryptographic systems (use classical crypto libraries)

---

## Next Steps

- Try `../CryptographicAnalysis/` - RSA factorization using Shor's algorithm
- Try `../PhaseEstimation/` - Eigenvalue estimation for quantum chemistry

---

## Resources

- [Quantum Fourier Transform (Wikipedia)](https://en.wikipedia.org/wiki/Quantum_Fourier_transform)
- [RSA Cryptosystem (Wikipedia)](https://en.wikipedia.org/wiki/RSA_(cryptosystem))
- [Draper Adder (QFT-based addition)](https://arxiv.org/abs/quant-ph/0008033)
