# Topological Quantum Program Format Specification

**Version:** 0.1 (Draft)  
**Date:** 2025-12-06  
**Status:** Proposal

## Motivation

Unlike gate-based quantum computing (which has OpenQASM, Quil, etc.), there is currently **no standardized format** for representing topological quantum programs. This document proposes a simple text-based format for serializing topological quantum computations.

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
# Comments start with #
ANYON <type>           # Declare anyon type (required first line)
INIT <count>           # Initialize anyons
BRAID <index>          # Braid operation
MEASURE <index>        # Fusion measurement
FMOVE <direction> <depth>  # F-move (basis change)
```

### Example: Bell State

```tqp
# Create entangled Bell-like state with Ising anyons
ANYON Ising
INIT 4
BRAID 0    # Braid anyons 0-1
BRAID 2    # Braid anyons 2-3
MEASURE 0  # Measure fusion
```

### Example: Simple Fusion Test

```tqp
# Test Ising fusion rule: σ × σ = 1 + ψ
ANYON Ising
INIT 2
MEASURE 0  # Should give Vacuum or Psi
```

### Example: Fibonacci Braiding

```tqp
# Fibonacci anyon example
ANYON Fibonacci
INIT 3
BRAID 0
BRAID 1
MEASURE 0
```

## Formal Grammar (EBNF)

```ebnf
program       ::= anyon_decl (operation)*
anyon_decl    ::= "ANYON" anyon_type
anyon_type    ::= "Ising" | "Fibonacci" | "SU2_" digit+

operation     ::= init | braid | measure | fmove
init          ::= "INIT" digit+
braid         ::= "BRAID" digit+
measure       ::= "MEASURE" digit+
fmove         ::= "FMOVE" direction digit+

direction     ::= "Left" | "Right" | "Up" | "Down"
digit         ::= "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9"

comment       ::= "#" (any character)* newline
```

## Implementation Plan

### Phase 1: Parser (F# Parser Combinators)

```fsharp
module TopologicalFormat.Parser

type TopologicalProgram = {
    AnyonType: AnyonSpecies.AnyonType
    Operations: TopologicalOperation list
}

let parseProgram (text: string) : Result<TopologicalProgram, string>

let parseFile (path: string) : Result<TopologicalProgram, string>
```

### Phase 2: Serializer

```fsharp
module TopologicalFormat.Serializer

let serializeProgram (program: TopologicalProgram) : string

let serializeToFile (program: TopologicalProgram) (path: string) : unit
```

### Phase 3: Integration with Backend

```fsharp
// Load and execute .tqp file
let executeFile backend filePath = task {
    match TopologicalFormat.Parser.parseFile filePath with
    | Ok program ->
        // Execute program on backend
        ...
    | Error msg ->
        return Error (TopologicalError.ValidationError msg)
}
```

## JSON Format (Alternative)

For machine-to-machine communication, JSON format:

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
INIT 4
BRAID 0
BRAID 2
MEASURE 0
```

## Future Extensions

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
- [ ] Parser implementation (`TopologicalFormat.Parser.fs`)
- [ ] Serializer implementation (`TopologicalFormat.Serializer.fs`)
- [ ] JSON schema validation
- [ ] CLI tool (`dotnet tqp run program.tqp`)
- [ ] Integration with `ITopologicalBackend`
- [ ] Unit tests
- [ ] Documentation examples

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
