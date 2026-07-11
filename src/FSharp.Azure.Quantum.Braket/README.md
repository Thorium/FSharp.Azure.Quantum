# FSharp.Azure.Quantum.Braket

**AWS Braket plugin for FSharp.Azure.Quantum**

Submits circuits to AWS Braket gate QPUs and simulators via OpenQASM 3.0, and neutral-atom
analog programs to QuEra Aquila via the Braket AHS format. This is the **only** package that
depends on the AWS SDK — the core `FSharp.Azure.Quantum` package stays AWS-free.

## Supported devices

| Kind | Devices (`Braket.Devices.*`) | Format |
|------|------------------------------|--------|
| Gate QPUs | `ionqAria1`, `ionqForte1`, `rigettiAnkaa3`, `iqmGarnet`, `oqcLucy`, `infleqtionSqale` | OpenQASM 3.0 |
| Managed simulators | `sv1`, `dm1`, `tn1` | OpenQASM 3.0 |
| Neutral-atom analog QPU | `queraAquila` | AHS |

Device ARNs track the Braket console — update `Braket.Devices` if a device is retired or added.

## Installation

```bash
dotnet add package FSharp.Azure.Quantum.Braket
```

This package requires `FSharp.Azure.Quantum` (pulled in transitively) and the `AWSSDK.Braket`
and `AWSSDK.S3` packages. You need AWS credentials with Braket access and an S3 bucket where
Braket writes task results.

## Quick start

### Gate circuits (any Braket gate device)

`BraketBackend` implements the shared `IQuantumBackend`, so it works with any gate device by ARN
and plugs into the standard algorithms (Grover, QFT, Shor, HHL).

```fsharp
open Amazon.Braket
open Amazon.S3
open FSharp.Azure.Quantum.Braket
open FSharp.Azure.Quantum.Braket.BraketExecution

let braket = new AmazonBraketClient()
let s3 = new AmazonS3Client()
let s3Config = { Bucket = "my-braket-results"; KeyPrefix = "tasks" }

// Pick a device by ARN — simulator, IonQ, Rigetti, IQM, OQC, Infleqtion, ...
let backend = BraketBackend(braket, s3, s3Config, Braket.Devices.sv1, shots = 1000)

// Run a gate circuit (wrap a CircuitBuilder.Circuit with CircuitWrapper)
let result = (backend :> IQuantumBackend).ExecuteToState circuit
```

The backend exports the circuit to OpenQASM 3.0, submits the task, polls to completion, reads the
result JSON from S3, and returns an approximate `QuantumState` built from the measurement
histogram. Failures come back as `Error (QuantumError ...)` rather than exceptions.

### Neutral-atom analog programs (QuEra Aquila)

```fsharp
open FSharp.Azure.Quantum.Algorithms.NeutralAtom

// Build a RydbergProgram, then submit it via the AHS format
let! result =
    submitAhsAsync braket s3 s3Config Braket.Devices.queraAquila program shots ct
```

## Modules

- **`Braket`** — pure helpers with no AWS SDK dependency (device ARNs, OpenQASM action wrapping,
  gate-result parsing). Testable in isolation.
- **`BraketExecution`** — the AWS-facing layer: `submitActionAsync`, `submitAhsAsync`, and the
  `BraketBackend` gate `IQuantumBackend`.

## Notes

- `ExecuteToState` reconstructs a `QuantumState` from the measurement histogram in three tiers:
  a dense state vector up to 20 qubits, a `SparseState` (observed outcomes only) for 21–31
  qubits, and `QuantumState.MeasurementHistogram` (bitstring → count) beyond that. The
  histogram tier has no width limit — it holds at most `shots` entries regardless of qubit
  count, so wide devices (Rigetti Ankaa, QuEra-scale) work. To get the raw histogram at any
  width, call `BraketBackend.ExecuteToHistogramAsync (circuit, ct)`.
- `ApplyOperation` is not supported (Braket runs whole circuits, not incremental gates); use
  `ExecuteToState` with a complete circuit.

## License

Same as the parent project (FSharp.Azure.Quantum) — Unlicense / Public Domain.
