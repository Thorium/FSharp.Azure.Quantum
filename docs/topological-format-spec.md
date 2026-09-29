# Topological Quantum Program Format Specification

**Version:** 0.1 (Draft)  
**Date:** 2025-12-06  
**Status:** Text format implemented in `FSharp.Azure.Quantum.Topological` (`TopologicalFormat` module); JSON form and extensions are proposals

## Motivation

Unlike gate-based quantum computing (which has OpenQASM, Quil, etc.), there is currently **no standardized format** for representing topological quantum programs. This document describes a simple text-based format for serializing topological quantum computations.

## Goals

1. **Human-readable**: Easy to write and understand
2. **Machine-parseable**: Simple to parse programmatically
3. **Anyon-agnostic**: Support different anyon theories (Ising, Fibonacci, etc.)
4. **Educational**: Help developers learn topological QC concepts

## Format Specification

### File Extension

`.tqp` (Topological Quantum Program)

### Structure

```
# Comments start with # and occupy a whole line
ANYON <type>
INIT <qubits>
BRAID <index>
MEASURE <index>
FMOVE <direction> <depth>
```

| Line | Meaning |
|------|---------|
| `ANYON <type>` | Anyon theory: `Ising`, `Fibonacci` or `SU2_<k>`. Required; by convention the first line. |
| `INIT <qubits>` | Number of logical qubits (≥ 1), prepared in \|0…0⟩. The backend chooses the anyons: Ising uses one σ pair per qubit plus one parity pair (2n + 2 anyons), Fibonacci one τ pair per qubit (2n), SU(2)_k one j=½ pair per qubit (2n, k ≥ 2). |
| `BRAID <index>` | Exchange anyons `index` and `index + 1`. |
| `MEASURE <index>` | Fusion measurement of anyons `index` and `index + 1`. |
| `FMOVE <direction> <depth>` | F-move (basis change) at the given tree depth; direction `Left`, `Right`, `Up` or `Down`. |

Keywords are upper case. The anyon type and the F-move direction are case-insensitive. A comment must be on its own line: a trailing `# ...` after an operation is a parse error.

### Example: Bell State

```tqp
# Entangled two-qubit state with Ising anyons
# (six sigma anyons: qubit 0, qubit 1, parity pair)
ANYON Ising
INIT 2
# Braids 0 and 2 act inside a qubit's pair and only add a phase
BRAID 0
BRAID 2
# Braid 1 exchanges anyons of two different pairs and entangles
BRAID 1
# Fusion measurement of qubit 0's pair
MEASURE 0
```

### Example: Simple Fusion Test

```tqp
# Ising fusion rule: σ × σ = 1 + ψ
ANYON Ising
INIT 1
# Qubit 0 starts in |0> (its pair fuses to vacuum); braid 1 mixes it with the parity pair
BRAID 1
# Now the pair can fuse to Vacuum or Psi
MEASURE 0
```

### Example: Fibonacci Braiding

```tqp
# Fibonacci anyon example: 3 qubits = 6 tau anyons
ANYON Fibonacci
INIT 3
BRAID 0
BRAID 1
MEASURE 0
```

## Formal Grammar (EBNF)

```ebnf
program       ::= (line newline)*
line          ::= anyon_decl | operation | comment | blank
anyon_decl    ::= "ANYON" anyon_type
anyon_type    ::= "Ising" | "Fibonacci" | "SU2_" digit+      (case-insensitive)

operation     ::= init | braid | measure | fmove
init          ::= "INIT" digit+                             (count ≥ 1)
braid         ::= "BRAID" digit+
measure       ::= "MEASURE" digit+
fmove         ::= "FMOVE" direction digit+

direction     ::= "Left" | "Right" | "Up" | "Down"           (case-insensitive)
digit         ::= "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9"

comment       ::= "#" (any character)*
```

Exactly one `ANYON` line is used: the first non-comment line that starts with `ANYON`. An executable program also needs an `INIT` line; if there are several, the first one is used.

## Implementation

The format lives in the `FSharp.Azure.Quantum.Topological.TopologicalFormat` module:

| Member | Purpose |
|--------|---------|
| `Program` | `{ AnyonType: AnyonSpecies.AnyonType; Operations: Operation list }` |
| `Operation` | `Initialize of count` \| `Braid of leftIndex` \| `Measure of leftIndex` \| `FMove of FMoveDirection * depth` \| `Comment of text` |
| `Parser.parseProgram`, `Parser.parseFile`, `Parser.parseFileAsync` | Text or file → `Result<Program, string>` (error messages carry the line number) |
| `Serializer.serializeProgram` | `Program` → text, with a generated header comment and the program's comments kept |
| `Serializer.serializeToFile`, `Serializer.serializeToFileAsync` | Write a program; return `Result<unit, string>` |
| `Executor.executeProgram`, `Executor.executeFile` (and `...Async`) | Run a program on any `IQuantumBackend`; return `Result<Executor.ExecutionResult, QuantumError>` |

```fsharp
open FSharp.Azure.Quantum.Topological
open FSharp.Azure.Quantum.Topological.TopologicalFormat

let source = """
ANYON Ising
INIT 2
BRAID 0
BRAID 2
BRAID 1
MEASURE 0
"""

// A topological backend that allows up to 10 anyons (enough for 4 Ising qubits)
let backend = TopologicalUnifiedBackendFactory.createIsing 10

match Parser.parseProgram source with
| Ok program ->
    printfn "%s" (Serializer.serializeProgram program)

    match Executor.executeProgram backend program with
    | Ok result -> result.Messages |> List.iter (printfn "%s")
    | Error err -> printfn "Execution failed: %s" err.Message
| Error msg -> printfn "Parse error: %s" msg
```

The executor calls `backend.InitializeState` with the `INIT` count and then applies each operation with `backend.ApplyOperation`. `FMOVE Left`/`Up` map to `FMoveDirection.Forward` and `Right`/`Down` to `FMoveDirection.Backward`. `MEASURE` splits the state into its fusion-outcome branches without picking one, so `ExecutionResult.MeasurementOutcomes` is currently always empty; sample the result with `QuantumState.measure result.FinalState shots` to get outcomes.

## JSON Format (Proposal, not implemented)

For machine-to-machine communication, a JSON form could look like this:

```json
{
  "version": "0.1",
  "anyonType": "Ising",
  "operations": [
    { "type": "init", "count": 4 },
    { "type": "braid", "leftIndex": 0 },
    { "type": "braid", "leftIndex": 2 },
    { "type": "measure", "leftIndex": 0 }
  ]
}
```

### JSON Schema

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "Topological Quantum Program",
  "type": "object",
  "required": ["version", "anyonType", "operations"],
  "properties": {
    "version": {
      "type": "string",
      "pattern": "^\\d+\\.\\d+$"
    },
    "anyonType": {
      "type": "string",
      "enum": ["Ising", "Fibonacci", "SU2_2", "SU2_3"]
    },
    "operations": {
      "type": "array",
      "items": {
        "oneOf": [
          {
            "type": "object",
            "required": ["type", "count"],
            "properties": {
              "type": { "const": "init" },
              "count": { "type": "integer", "minimum": 1 }
            }
          },
          {
            "type": "object",
            "required": ["type", "leftIndex"],
            "properties": {
              "type": { "const": "braid" },
              "leftIndex": { "type": "integer", "minimum": 0 }
            }
          },
          {
            "type": "object",
            "required": ["type", "leftIndex"],
            "properties": {
              "type": { "const": "measure" },
              "leftIndex": { "type": "integer", "minimum": 0 }
            }
          }
        ]
      }
    }
  }
}
```

## Comparison with OpenQASM

| Feature | OpenQASM 3.0 | Topological Format (Proposed) |
|---------|--------------|-------------------------------|
| **Paradigm** | Gate-based circuits | Anyon braiding |
| **State Model** | Qubit amplitudes | Fusion trees |
| **Operations** | H, CNOT, RZ, etc. | Braid, Measure, FMove |
| **Registers** | Classical/Quantum bits | Anyon indices |
| **Control Flow** | if/while/for | Not yet (future) |
| **Measurements** | Projective (computational basis) | Fusion (topological charge) |
| **File Extension** | `.qasm` | `.tqp` (proposed) |

### OpenQASM 3.0 Bell State:

```qasm
OPENQASM 3.0;
qubit[2] q;
bit[2] c;

h q[0];
cx q[0], q[1];
c = measure q;
```

### Topological Format Bell State:

```tqp
ANYON Ising
INIT 2
BRAID 0
BRAID 2
BRAID 1
MEASURE 0
```

## Future Extensions

None of these are implemented; the parser rejects them today.

### 1. Variables and Loops

```tqp
ANYON Ising
INIT 6
VAR i = 0
WHILE i < 3
    BRAID i
    i = i + 1
END
MEASURE 0
```

### 2. Parameterized Braiding

```tqp
ANYON Ising
INIT 4
BRAID 0 ANGLE 0.5π  # Partial braid (for adiabatic evolution)
```

### 3. Conditional Operations

```tqp
ANYON Ising
INIT 4
BRAID 0
MEASURE 0 -> outcome
IF outcome == Vacuum THEN
    BRAID 2
ELSE
    BRAID 1
END
```

## Implementation Status

- [x] Conceptual design
- [x] Parser implementation (`TopologicalFormat.Parser` module)
- [x] Serializer implementation (`TopologicalFormat.Serializer` module)
- [ ] JSON format and schema validation (proposal only)
- [ ] CLI tool (`dotnet tqp run program.tqp`) (future enhancement)
- [x] Integration with `IQuantumBackend` (`TopologicalFormat.Executor` module)
- [ ] Classical measurement outcomes from `MEASURE` in `ExecutionResult.MeasurementOutcomes`
- [x] Unit tests (`TopologicalFormatTests.fs`)
- [x] Documentation examples (`examples/Topological/FormatDemo.fsx`, `bell-state.tqp`)

## Related Standards

- **OpenQASM 3.0**: https://openqasm.com/
- **Quil**: https://github.com/quil-lang/quil
- **Braid Theory**: Jones, V. F. R. (1987). "Hecke algebra representations of braid groups"

## Contributing

This is a **draft proposal**. Feedback welcome on:
1. Syntax clarity
2. Missing operations
3. Compatibility with existing tools
4. Mathematical correctness

## License

This specification is released under CC0 (public domain) to encourage adoption.

---

**Note**: This format is designed for the `FSharp.Azure.Quantum.Topological` library but could be adopted by other topological QC implementations.
