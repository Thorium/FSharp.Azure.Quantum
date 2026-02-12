# Textbook Audit Gap Implementation Plan

## Overview

An audit of the FSharp.Azure.Quantum library against Chapter 14 (Quantum Computing) of a Computer Science textbook identified three coverage gaps. This document provides the implementation plan to close them.

The library scores excellently overall: all 10 textbook algorithms are implemented and mathematically verified, all 11 gate types are present with exact matrix matches, and zero mathematical errors were found. The three gaps below are the only missing textbook topics.

| # | Gap | Estimated Size | Dependencies | Priority |
|---|-----|---------------|--------------|----------|
| 1 | Superdense Coding | ~300-400 lines + tests | BellStates.fs (exists) | Medium |
| 2 | Ekert (E91) QKD | ~600-800 lines + tests | BellStates.fs, BB84 patterns (exist) | Medium |
| 3 | Quantum Error Correction Codes | ~800-1000 lines + tests | Gates.fs, measurement (exist) | High |

---

## Constraints and Rules

All implementations must follow these established library conventions:

- **Rule 1**: All public functions take `IQuantumBackend` as parameter. This is an Azure Quantum library, not a standalone solver library.
- **Intent-Plan-Execute**: Each module validates backend type, rejects `Annealing`, and plans operations before execution.
- **Result types**: All public functions return `Result<'T, QuantumError>` using the `result { }` computation expression.
- **TDD**: Each implementation phase follows RED-GREEN-REFACTOR cycles.
- **Quality Gates**: Zero security warnings, all tests pass, `dotnet format whitespace` applied.
- **`.fsproj` order**: Files inserted after their dependencies in compilation order.
- **File size target**: 300-1000 lines per file (AI-optimized context density).
- **Namespace**: `FSharp.Azure.Quantum.Algorithms` (same as all other algorithm files).

---

## Gap 1: Superdense Coding

### Background

Superdense coding is the dual of quantum teleportation. Teleportation sends 1 qubit of quantum information using 2 classical bits + 1 shared entangled qubit. Superdense coding sends 2 classical bits using 1 qubit + 1 shared entangled qubit.

The textbook mentions it as a key quantum communication protocol. The library currently only references it in comments (`BellStates.fs` line 155, `BellStatesExample.fsx` line 47) but has no implementation.

### Protocol Steps

1. **Preparation**: Alice and Bob share a Bell pair |Phi+> = (|00>+|11>)/sqrt(2)
2. **Encoding**: Alice applies one of 4 operations to her qubit based on 2 classical bits:
   - `00` -> I (identity, do nothing)
   - `01` -> X (bit flip)
   - `10` -> Z (phase flip)
   - `11` -> ZX (both)
3. **Transmission**: Alice sends her qubit to Bob (1 qubit carries 2 bits)
4. **Decoding**: Bob performs Bell measurement (CNOT -> H -> Measure both)
5. **Result**: Bob recovers the 2 classical bits

### Files to Create/Modify

| File | Action | Location |
|------|--------|----------|
| `SuperdenseCoding.fs` | **Create** | `src/FSharp.Azure.Quantum/Algorithms/` |
| `FSharp.Azure.Quantum.fsproj` | **Edit** | Insert after `BellStates.fs` (line 149) |
| `SuperdenseCodingTests.fs` | **Create** | `tests/FSharp.Azure.Quantum.Tests/` |
| `FSharp.Azure.Quantum.Tests.fsproj` | **Edit** | Insert in test compilation order |
| `SuperdenseCodingExample.fsx` | **Create** | `examples/Algorithms/` |

### Module Structure

```
SuperdenseCoding.fs (~300-400 lines)

namespace FSharp.Azure.Quantum.Algorithms
open FSharp.Azure.Quantum.CircuitBuilder
open FSharp.Azure.Quantum.Core
open FSharp.Azure.Quantum.Core.BackendAbstraction

module SuperdenseCoding =

    // ================================================================
    // TYPES
    // ================================================================
    type ClassicalMessage = { Bit1: int; Bit2: int }
    type SuperdenseResult = {
        SentMessage: ClassicalMessage
        ReceivedMessage: ClassicalMessage
        Success: bool
        Fidelity: float
    }
    type SuperdenseStatistics = { ... }

    // ================================================================
    // INTENT -> PLAN -> EXECUTE
    // ================================================================
    type private SuperdenseIntent = {
        AliceQubit: int       // 0
        BobQubit: int         // 1
        Message: ClassicalMessage
    }
    type private SuperdensePlan =
        | ExecuteViaOps of bellOps * encodeOps * decodeOps
    let private plan (backend: IQuantumBackend) (intent) : Result<...>
    let private executePlan (backend: IQuantumBackend) (state) (plan) : Result<...>

    // ================================================================
    // ENCODING (Alice's operations)
    // ================================================================
    let private encodeMessage (msg: ClassicalMessage) (aliceQubit: int)
        // 00 -> [], 01 -> [X], 10 -> [Z], 11 -> [Z; X]

    // ================================================================
    // MAIN PROTOCOL
    // ================================================================
    let send (backend: IQuantumBackend) (message: ClassicalMessage)
        : Result<SuperdenseResult, QuantumError>

    // ================================================================
    // CONVENIENCE FUNCTIONS
    // ================================================================
    let send00, send01, send10, send11

    // ================================================================
    // STATISTICS
    // ================================================================
    let runStatistics (backend) (message) (trials)
        : Result<SuperdenseStatistics, QuantumError>

    // ================================================================
    // FORMATTING
    // ================================================================
    let formatResult (result: SuperdenseResult) : string
```

### Test Plan

| Test | What It Verifies |
|------|-----------------|
| `Send00_ReturnsCorrectBits` | Identity encoding -> Bob measures 00 |
| `Send01_ReturnsCorrectBits` | X encoding -> Bob measures 01 |
| `Send10_ReturnsCorrectBits` | Z encoding -> Bob measures 10 |
| `Send11_ReturnsCorrectBits` | ZX encoding -> Bob measures 11 |
| `AllFourMessages_DistinctResults` | Each message maps to a unique measurement outcome (anti-gaming) |
| `HighFidelity_OnLocalBackend` | Fidelity > 0.99 on noise-free backend |
| `RejectsAnnealingBackend` | Returns `OperationError` for annealing backends |
| `Statistics_AllTrialsSucceed` | Multiple trials all return correct message |

---

## Gap 2: Ekert (E91) QKD Protocol

### Background

An entanglement-based quantum key distribution protocol. Unlike BB84 (which uses prepare-and-measure), E91 derives security from Bell inequality violation (CHSH inequality). If an eavesdropper intercepts, the Bell inequality violation decreases, alerting Alice and Bob.

The textbook mentions E91 alongside BB84 as a key QKD protocol. The library currently only references it in comments (`BellStates.fs` line 16, `BellStatesExample.fsx` line 10) but has no implementation. All building blocks exist: Bell state creation, rotated measurement, and the BB84 module provides architectural patterns.

### Protocol Steps

1. **Entanglement Source**: Generate Bell pairs |Phi+> = (|00>+|11>)/sqrt(2), distribute one qubit to Alice, one to Bob
2. **Basis Choice**: Each party randomly chooses a measurement basis from a set:
   - Alice: {0 deg, 45 deg, 90 deg} (3 bases)
   - Bob: {0 deg, 45 deg, 135 deg} (3 bases, note the asymmetry)
3. **Measurement**: Both measure their qubits in their chosen bases
4. **Public Comparison**: Announce bases (not results)
5. **Key Sifting**: When Alice and Bob used the **same** basis (0 deg or 45 deg), their results are perfectly anti-correlated -> use as key bits
6. **Security Check (CHSH)**: When they used **different** bases, compute the CHSH correlation parameter S:
   - S = E(a1,b1) - E(a1,b3) + E(a3,b1) + E(a3,b3)
   - Quantum mechanics predicts |S| = 2*sqrt(2) ~ 2.828
   - Local hidden variables (classical bound): |S| <= 2
   - If |S| < 2*sqrt(2), eavesdropper detected
7. **Key Generation**: Sifted key bits from matching bases

### Files to Create/Modify

| File | Action | Location |
|------|--------|----------|
| `EkertQKD.fs` | **Create** | `src/FSharp.Azure.Quantum/Algorithms/` |
| `FSharp.Azure.Quantum.fsproj` | **Edit** | Insert after `QuantumKeyDistribution.fs` (line 151) |
| `EkertQKDTests.fs` | **Create** | `tests/FSharp.Azure.Quantum.Tests/` |
| `FSharp.Azure.Quantum.Tests.fsproj` | **Edit** | Insert in test compilation order |
| `EkertQKDExample.fsx` | **Create** | `examples/Algorithms/` |

### Module Structure

```
EkertQKD.fs (~600-800 lines)

namespace FSharp.Azure.Quantum.Algorithms
module EkertQKD =

    // ================================================================
    // TYPES
    // ================================================================
    type AliceBasis = Degrees0 | Degrees45 | Degrees90
    type BobBasis = Degrees0 | Degrees45 | Degrees135

    type E91Pair = {
        AliceBasis: AliceBasis
        BobBasis: BobBasis
        AliceResult: int  // 0 or 1
        BobResult: int    // 0 or 1
    }

    type CHSHResult = {
        S: float                 // CHSH parameter
        QuantumBound: float      // 2*sqrt(2)
        ClassicalBound: float    // 2.0
        IsSecure: bool           // |S| > classical bound
        EavesdropperDetected: bool
    }

    type E91Result = {
        TotalPairs: int
        KeyBits: int list
        SiftedKeyLength: int
        CHSHTest: CHSHResult
        KeyRate: float
        IsSecure: bool
    }

    // ================================================================
    // INTENT -> PLAN -> EXECUTE
    // ================================================================
    type private E91Intent = {
        NumPairs: int
        AliceQubit: int
        BobQubit: int
    }
    type private E91Plan = | ExecuteViaOps of ...

    // ================================================================
    // MEASUREMENT IN ROTATED BASIS
    // ================================================================
    let private measureInBasis (basis: float) (qubit: int)
        // Apply Ry(-angle) before measuring in Z-basis

    // ================================================================
    // CHSH CORRELATION
    // ================================================================
    let private computeCorrelation (pairs: E91Pair list)
        (aliceBasis) (bobBasis) : float
        // E(a,b) = P(same) - P(different)

    let private computeCHSH (pairs: E91Pair list) : CHSHResult
        // S = E(a1,b1) - E(a1,b3) + E(a3,b1) + E(a3,b3)

    // ================================================================
    // MAIN PROTOCOL
    // ================================================================
    let run (backend: IQuantumBackend) (numPairs: int)
        : Result<E91Result, QuantumError>

    // ================================================================
    // EVE ATTACKS (Intercept-Resend on entangled pairs)
    // ================================================================
    type EveAttack = | InterceptResend | MeasureAndResend
    let runWithEve (backend) (numPairs) (attack)
        : Result<E91Result, QuantumError>

    // ================================================================
    // FORMATTING
    // ================================================================
    let formatResult (result: E91Result) : string
    let formatCHSH (chsh: CHSHResult) : string
```

### Test Plan

| Test | What It Verifies |
|------|-----------------|
| `NoEavesdropper_CHSHViolated` | S ~ 2*sqrt(2) when no Eve present |
| `NoEavesdropper_KeyBitsCorrelated` | Alice and Bob's key bits match on same-basis measurements |
| `WithEavesdropper_CHSHReduced` | Eve's presence reduces S towards classical bound |
| `SiftedKeyRate_ApproximatelyCorrect` | ~2/9 of pairs used for key (2 matching bases out of 3x3=9 combinations) |
| `SecurityDetection_ClassicalBound` | `IsSecure = false` when S <= 2.0 |
| `AllBasisCombinations_Used` | Measurements span all 9 basis combinations |
| `RejectsAnnealingBackend` | Returns error for wrong backend type |
| `MeasurementBasis_CorrectRotations` | 0 deg -> Z, 45 deg -> diagonal, 90 deg -> X, 135 deg -> anti-diagonal |

---

## Gap 3: Quantum Error Correction Codes

### Background

Quantum error correction (QEC) protects quantum information from noise by encoding a single logical qubit into multiple physical qubits. The textbook covers three codes in detail:

1. **3-qubit bit-flip code**: Protects against X (bit-flip) errors
2. **Shor 9-qubit code**: Protects against arbitrary single-qubit errors
3. **Steane 7-qubit code**: CSS code, protects against arbitrary errors more efficiently

The library currently has error **mitigation** (ZNE, PEC, readout) and the toric code in the topological plugin, but no gate-based quantum error correction codes. The textbook devotes significant coverage to QEC including the no-cloning theorem, discretization of errors, and the fault-tolerance threshold.

### Key Concepts

- **Encoding**: |0>\_L -> encoded state across multiple physical qubits, |1>\_L -> encoded state
- **Syndrome measurement**: Detect which error occurred without collapsing the logical state
- **Error correction**: Apply corrective operation based on syndrome
- **No-cloning**: Cannot copy quantum states, so redundancy uses entanglement instead

### Codes to Implement

**3-qubit bit-flip code** (simplest, textbook's primary example):
- Encoding: |0> -> |000>, |1> -> |111>
- Syndrome: measure Z1Z2 and Z2Z3 (parity checks)
- Correction: syndrome 00 -> no error, 01 -> flip q3, 10 -> flip q1, 11 -> flip q2

**3-qubit phase-flip code** (dual of bit-flip):
- Encoding: |0> -> |+++>, |1> -> |--->
- Apply H on all 3 qubits, then bit-flip code structure, then H on all 3

**Shor 9-qubit code** (concatenation of phase-flip + bit-flip):
- Encoding: |0> -> (|000>+|111>)(|000>+|111>)(|000>+|111>) / 2*sqrt(2)
- 8 syndrome measurements, corrects any single-qubit error

**Steane 7-qubit code** (CSS code, [[7,1,3]]):
- Based on classical [7,4,3] Hamming code
- 6 stabilizer generators (3 X-type, 3 Z-type)
- Encoding via stabilizer projection

### Files to Create/Modify

| File | Action | Location |
|------|--------|----------|
| `QuantumErrorCorrection.fs` | **Create** | `src/FSharp.Azure.Quantum/Algorithms/` |
| `FSharp.Azure.Quantum.fsproj` | **Edit** | Insert after `TrotterSuzuki.fs` (line 158) |
| `QuantumErrorCorrectionTests.fs` | **Create** | `tests/FSharp.Azure.Quantum.Tests/` |
| `FSharp.Azure.Quantum.Tests.fsproj` | **Edit** | Insert in test compilation order |
| `QuantumErrorCorrectionExample.fsx` | **Create** | `examples/Algorithms/` |

### Module Structure

```
QuantumErrorCorrection.fs (~800-1000 lines)

namespace FSharp.Azure.Quantum.Algorithms
module QuantumErrorCorrection =

    // ================================================================
    // TYPES
    // ================================================================
    type ErrorType = | BitFlip | PhaseFlip | Combined
    type Syndrome = { Bits: int list }
    type ErrorCode =
        | BitFlipCode3    // [[3,1,1]] bit-flip
        | PhaseFlipCode3  // [[3,1,1]] phase-flip
        | ShorCode9       // [[9,1,3]] arbitrary single-qubit
        | SteaneCode7     // [[7,1,3]] CSS code

    type CodeParameters = {
        Code: ErrorCode
        PhysicalQubits: int
        LogicalQubits: int
        Distance: int      // minimum weight of undetectable error
    }

    type EncodingResult = {
        Code: ErrorCode
        PhysicalQubits: int
        LogicalQubitIndex: int
        EncodedState: QuantumState
    }

    type SyndromeResult = {
        Syndrome: Syndrome
        DetectedError: ErrorType option
        ErrorQubit: int option
    }

    type CorrectionResult = {
        SyndromeResult: SyndromeResult
        CorrectedState: QuantumState
        CorrectionApplied: bool
    }

    // ================================================================
    // CODE PARAMETERS
    // ================================================================
    let codeParameters (code: ErrorCode) : CodeParameters

    // ================================================================
    // INTENT -> PLAN -> EXECUTE
    // ================================================================
    type private QecIntent = {
        Code: ErrorCode
        LogicalState: int  // 0 or 1
    }

    // ================================================================
    // 3-QUBIT BIT-FLIP CODE
    // ================================================================
    module BitFlip =
        let encode (backend) (state) : Result<EncodingResult, QuantumError>
            // |psi> -> CNOT(0,1) -> CNOT(0,2)
        let measureSyndrome (backend) (state)
            : Result<SyndromeResult, QuantumError>
            // Measure Z0Z1, Z1Z2 via ancilla qubits
        let correct (backend) (state) (syndrome)
            : Result<CorrectionResult, QuantumError>
            // Apply X to identified qubit
        let roundTrip (backend) (logicalBit) (errorQubit option)
            : Result<CorrectionResult, QuantumError>
            // Full encode -> inject error -> syndrome -> correct -> verify

    // ================================================================
    // 3-QUBIT PHASE-FLIP CODE
    // ================================================================
    module PhaseFlip =
        let encode (backend) (state) : Result<...>
            // H on all 3 -> bit-flip encoding structure
        let measureSyndrome (backend) (state) : Result<...>
            // X-basis syndrome: H -> Z-syndrome -> H
        let correct (backend) (state) (syndrome) : Result<...>
        let roundTrip (backend) (logicalBit) (errorQubit option)
            : Result<...>

    // ================================================================
    // SHOR 9-QUBIT CODE
    // ================================================================
    module Shor =
        let encode (backend) (state) : Result<...>
            // Phase-flip encoding on 3 blocks,
            // then bit-flip each block
        let measureSyndrome (backend) (state) : Result<...>
            // 8 syndrome bits: 6 for bit-flip (2 per block)
            // + 2 for phase-flip
        let correct (backend) (state) (syndrome) : Result<...>
        let roundTrip (backend) (logicalBit) (errorType) (errorQubit)
            : Result<...>

    // ================================================================
    // STEANE 7-QUBIT CODE
    // ================================================================
    module Steane =
        let encode (backend) (state) : Result<...>
            // Hamming [7,4,3]-based CSS encoding
        let measureSyndrome (backend) (state) : Result<...>
            // 6 stabilizer measurements (3 X-type, 3 Z-type)
        let correct (backend) (state) (syndrome) : Result<...>
        let roundTrip (backend) (logicalBit) (errorType) (errorQubit)
            : Result<...>

    // ================================================================
    // ERROR INJECTION (for testing/demonstration)
    // ================================================================
    let injectBitFlip (backend) (qubit) (state) : Result<...>
    let injectPhaseFlip (backend) (qubit) (state) : Result<...>
    let injectArbitraryError (backend) (qubit) (theta) (phi) (state)
        : Result<...>

    // ================================================================
    // FORMATTING
    // ================================================================
    let formatCodeParameters (code: ErrorCode) : string
    let formatSyndrome (result: SyndromeResult) : string
    let formatCorrection (result: CorrectionResult) : string
```

### Test Plan

| Test | What It Verifies |
|------|-----------------|
| **Bit-Flip Code** | |
| `BitFlip_Encode0_Produces000` | \|0> encodes to \|000> |
| `BitFlip_Encode1_Produces111` | \|1> encodes to \|111> |
| `BitFlip_DetectsFlipOnQubit0` | Syndrome (1,0) -> error on q0 |
| `BitFlip_DetectsFlipOnQubit1` | Syndrome (1,1) -> error on q1 |
| `BitFlip_DetectsFlipOnQubit2` | Syndrome (0,1) -> error on q2 |
| `BitFlip_NoError_SyndromeZero` | Syndrome (0,0) -> no error |
| `BitFlip_RoundTrip_CorrectsBitFlip` | Full encode -> error -> correct -> verify |
| **Phase-Flip Code** | |
| `PhaseFlip_Encode0_ProducesPlusPlusPlus` | \|0> -> \|+++> |
| `PhaseFlip_DetectsPhaseFlip` | Z error detected and corrected |
| `PhaseFlip_RoundTrip_CorrectsPhaseFlip` | Full round-trip |
| **Shor Code** | |
| `Shor_CorrectsBitFlip` | X error on any qubit -> corrected |
| `Shor_CorrectsPhaseFlip` | Z error on any qubit -> corrected |
| `Shor_CorrectsArbitraryError` | Arbitrary single-qubit -> corrected |
| `Shor_TwoErrors_Undetectable` | 2 errors on same block -> fails (distance 3) |
| **Steane Code** | |
| `Steane_CorrectsBitFlip` | X error corrected |
| `Steane_CorrectsPhaseFlip` | Z error corrected |
| `Steane_CorrectsArbitraryError` | Arbitrary error corrected |
| **Cross-Cutting** | |
| `AllCodes_RejectAnnealingBackend` | Returns error for annealing |
| `CodeParameters_CorrectValues` | [[3,1,1]], [[9,1,3]], [[7,1,3]] |

---

## Implementation Order

The three gaps should be implemented in this order, as each builds in complexity:

```
Phase 1: Superdense Coding (simplest, ~1-2 TDD cycles)
  |-- Create SuperdenseCoding.fs (intent -> plan -> execute)
  |-- Create SuperdenseCodingTests.fs
  |-- Update .fsproj files
  |-- Build + test
  +-- Create SuperdenseCodingExample.fsx

Phase 2: Ekert E91 QKD (medium complexity, ~3-4 TDD cycles)
  |-- Create EkertQKD.fs (Bell pairs + rotated measurement + CHSH)
  |-- Create EkertQKDTests.fs
  |-- Update .fsproj files
  |-- Build + test
  +-- Create EkertQKDExample.fsx

Phase 3: Quantum Error Correction (most complex, ~5-6 TDD cycles)
  |-- TDD cycle 1: BitFlip submodule (encode + syndrome + correct)
  |-- TDD cycle 2: PhaseFlip submodule
  |-- TDD cycle 3: Shor 9-qubit code
  |-- TDD cycle 4: Steane 7-qubit code
  |-- TDD cycle 5: Error injection utilities
  |-- Update .fsproj files
  |-- Build + full test suite
  +-- Create QuantumErrorCorrectionExample.fsx
```

---

## Appendix: Audit Summary

For reference, the full audit found the following coverage:

### Confirmed Covered (verified correct)

- Deutsch-Jozsa algorithm
- QFT (with controlled-R\_k construction)
- Shor's factoring (full pipeline)
- Grover's search (with optimal iteration count)
- Quantum Phase Estimation
- Bell states (all 4, creation + measurement)
- Quantum teleportation
- BB84 QKD (full protocol + Eve attacks + error correction + privacy amplification)
- All standard gates: X, Y, Z, H, S, T, CNOT, SWAP, Toffoli (CCX), controlled-U, Fredkin (via decomposition)
- Amplitude amplification (generalization of Grover)
- Trotter-Suzuki decomposition (quantum simulation)

### Beyond Textbook (library has, textbook does not cover)

HHL algorithm, QAOA, VQE, topological QC (anyons, braids, toric code, Fibonacci anyons), quantum ML (kernels, SVM, VQC), quantum chemistry, drug discovery solvers, error mitigation (ZNE, PEC, readout), QUBO/Ising solvers, quantum arithmetic (Draper adder), multiple cloud backends (IonQ, Rigetti, Quantinuum, D-Wave), quantum Monte Carlo, state preparation.

### Gate Matrix Verification

All 11 textbook gate matrices were verified character-by-character against the library's `Gates.fs` implementation. Zero discrepancies found.
