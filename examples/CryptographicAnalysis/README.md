# Cryptographic Analysis Examples

**Business Use Case:** Security assessment of public-key cryptography against quantum attacks

---

## Overview

The `QuantumPeriodFinderBuilder` implements Shor's algorithm to factor composite integers, demonstrating the quantum threat to RSA encryption. This is valuable for:

1. **Security Consulting** - Assessing RSA key strength against quantum attacks
2. **Cryptographic Research** - Understanding post-quantum cryptography requirements
3. **Risk Management** - Planning for "Q-Day" when quantum computers break current crypto
4. **Education** - Teaching quantum cryptanalysis and Shor's algorithm

---

## Examples

### 1. RSA Factorization (Security Assessment)

**File:** `RSAFactorization.fsx`

**Scenario:** A security consulting firm analyzes RSA key strength against quantum computers using Shor's algorithm.

**What It Demonstrates:**
- Integer factorization using quantum phase estimation
- How Shor's algorithm breaks RSA encryption
- Quantum resource requirements (qubits, circuit depth)
- Real-world threat timeline assessment

**Run:**
```bash
dotnet fsi RSAFactorization.fsx
```

**Expected Output:**
```
=== RSA Security Analysis with Shor's Algorithm ===

Target RSA Modulus (n): 15
Running Shor's algorithm...

✅ SUCCESS: Factorization Found!
  Prime factors:        3 × 5 = 15

⚠️  RSA SECURITY BROKEN!
   With factors p=3 and q=5, an attacker can:
   1. Calculate φ(n) = (p-1)(q-1) = 8
   2. Derive private key d from public key e
   3. Decrypt all messages encrypted with this RSA key
```

---

## API Reference

### F# Computation Expression

```fsharp
open FSharp.Azure.Quantum
// open FSharp.Azure.Quantum.QuantumPeriodFinder

// Factor RSA modulus using Shor's algorithm
// let problem = periodFinder {
//     number 15             // Composite number to factor
//     precision 8           // QPE precision qubits
//     maxAttempts 10        // Probabilistic algorithm may need retries
// }

// match solve problem with
// | Ok result ->
//     match result.Factors with
//     | Some (p, q) -> 
//         printfn "Factors: %d × %d = %d" p q (p * q)
//     | None -> 
//         printfn "Period found but no factors (try again)"
// | Error msg -> 
//     printfn "Error: %s" msg
```

### F# Convenience Functions

```fsharp
// Simple factorization
// let problem1 = factorInteger 15 8           // Auto-selects random base

// Factor with specific base
// let problem2 = factorIntegerWithBase 15 7 8 // Use base a = 7

// RSA-breaking mode (crypto analysis)
// let problem3 = breakRSA 143                 // Optimal settings for n = 143
```

### C# API

```csharp
using static FSharp.Azure.Quantum.CSharpBuilders;

// Factor RSA modulus
var problem = FactorInteger(number: 15, precision: 8);
var result = ExecutePeriodFinder(problem);

if (result.IsOk) {
    var factors = result.ResultValue.Factors;
    if (factors.HasValue) {
        var (p, q) = factors.Value;
        Console.WriteLine($"Factors: {p} × {q}");
    }
}
```

---

## How Shor's Algorithm Works

### High-Level Overview

1. **Choose random base** `a < n` (coprime to n)
2. **Find period** `r` where `a^r ≡ 1 (mod n)` using quantum phase estimation
3. **Extract factors** using `gcd(a^(r/2) ± 1, n)`

### Quantum Advantage

| Method | Time Complexity | Example (n = 2048-bit RSA) |
|--------|-----------------|----------------------------|
| **Classical (GNFS)** | `exp(∛(log n))` | ~10^9 years |
| **Shor's Algorithm** | `(log n)^3` | ~8 hours (fault-tolerant QC) |

### Why RSA is Vulnerable

- RSA security relies on difficulty of factoring `n = p × q`
- Classical algorithms take exponential time
- Shor's algorithm runs in polynomial time on quantum computers
- Once `p` and `q` are known, private key `d` can be computed from public key `e`

---

## Qubit Requirements

| RSA Key Size | Modulus Bits | Qubits Needed | Current Status |
|--------------|--------------|---------------|----------------|
| Toy (n=15) | 4 bits | 8 qubits | ✅ Feasible (NISQ) |
| Small (n=143) | 8 bits | 16 qubits | ✅ Feasible (NISQ) |
| RSA-1024 | 1024 bits | ~2048 qubits | ❌ Not yet possible |
| RSA-2048 | 2048 bits | ~4096 qubits | ❌ Requires fault tolerance |
| RSA-4096 | 4096 bits | ~8192 qubits | ❌ Distant future |

**Current NISQ Hardware:**
- IBM Quantum: 127 qubits (2023)
- Google Sycamore: 70 qubits (2023)
- IonQ Aria: 25 qubits (2023)

---

## Security Recommendations

### For Organizations

1. **Monitor Quantum Progress**
   - Track qubit counts and error rates
   - Assess when RSA-2048 may be broken ("Q-Day")
   - Current estimate: 2030-2040 for fault-tolerant systems

2. **Adopt Post-Quantum Cryptography**
   - NIST standardized algorithms (2024):
     - CRYSTALS-Kyber (encryption)
     - CRYSTALS-Dilithium (signatures)
     - SPHINCS+ (stateless signatures)
   - Start transitioning hybrid systems (classical + post-quantum)

3. **Protect Long-Term Secrets**
   - Data encrypted today may be vulnerable when quantum computers arrive
   - "Harvest now, decrypt later" attacks
   - Use post-quantum crypto for data needing >10 year confidentiality

4. **Increase Key Sizes (Interim)**
   - Use RSA-4096 or RSA-8192 as temporary measure
   - Elliptic curve: Migrate to Curve448 or Curve25519

### For Developers

- ✅ Use this tool to understand quantum threat
- ✅ Educate stakeholders about post-quantum requirements
- ✅ Test integration of NIST post-quantum algorithms
- ❌ Don't use RSA for long-term data protection
- ❌ Don't assume current crypto is quantum-safe

---

## Important Notes

### ⚠️ Current Limitations

- **NISQ Hardware**: Can only factor toy examples (n < 1000)
- **Error Rates**: Real quantum hardware has ~1% gate error rates
- **No Threat Yet**: Current quantum computers cannot break real RSA
- **Educational Focus**: This tool is for learning and assessment, not real attacks

### 🎓 Educational Value

This builder is ideal for:
- Teaching Shor's algorithm and quantum cryptanalysis
- Understanding post-quantum cryptography motivation
- Security risk assessment and planning
- Demonstrating quantum advantage over classical algorithms

**Not for:**
- Breaking real RSA keys (requires fault-tolerant quantum computers)
- Production cryptanalysis (current hardware insufficient)
- Illegal activities (unauthorized decryption is a crime)

---

## Next Steps

- Try `../QuantumArithmetic/` - RSA encryption using modular arithmetic
- Try `../PhaseEstimation/` - Core quantum algorithm behind Shor's
- Read NIST Post-Quantum Cryptography standards

---

## Resources

- [Shor's Algorithm (Wikipedia)](https://en.wikipedia.org/wiki/Shor%27s_algorithm)
- [NIST Post-Quantum Cryptography](https://csrc.nist.gov/projects/post-quantum-cryptography)
- [Quantum Threat Timeline](https://globalriskinstitute.org/publication/2023-quantum-threat-timeline-report/)
- [RSA Cryptosystem Security](https://en.wikipedia.org/wiki/RSA_(cryptosystem)#Security_and_practical_considerations)
