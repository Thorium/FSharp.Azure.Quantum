# TKT-20: Implement: Layer 2 - Basic Circuit Builder

## Status
**State:** In Progress


## Description
Implement quantum circuit builder with gate types, circuit construction, basic optimizations, and backend compilation.

---

**Comment (2025-11-23 19:59:30)**: # Implement: Layer 2 - Basic Circuit Builder

**Objective**
Implement simplified quantum circuit builder with gate types, circuit construction, basic optimizations, and backend compilation to enable QAOA circuit generation for Azure Quantum.

**Context**
The library needs a circuit builder to construct quantum circuits for QAOA without the complexity of phantom types or units of measure. This simplified approach focuses on practical circuit construction and optimization for submission to Azure Quantum backends.

**Domain Implementation Rule**
⚠️ **CRITICAL**: Implement ALL circuit builder code in a SINGLE FILE `CircuitBuilder.fs`.

* Include: Gate types, circuit construction, optimizations, backend compilation - ALL in one file
* **Ignore F# conventions** that separate types and functions into different files
* **Context window optimization**: AI needs all circuit builder code visible simultaneously

**Task**
Implement circuit builder following TDD methodology:

* Write tests for gate types (X, Y, Z, H, CNOT, RX, RY, RZ)
* Implement circuit construction API (addGate, addGates, compose)
* Add basic optimizations (inverse gate removal, gate fusion)
* Build backend compilation (OpenQASM output)
* Add circuit validation (qubit bounds checking, gate compatibility)
* Validate correctness with circuit equivalence tests

**Implementation Approach**

* Follow test-driven development with comprehensive test coverage
* Use simple discriminated union for gate types
* Provide fluent API for circuit construction
* **Keep all circuit builder code in single file for AI context optimization**

**Technical Requirements**

* Support essential gate types: X, Y, Z, H, CNOT, RX, RY, RZ with parameters
* Circuit construction: addGate, addGates, compose operations
* Basic optimizations: remove inverse pairs (H-H, X-X), fuse RX rotations
* OpenQASM output generation for Azure Quantum submission
* Circuit validation: qubit range checking, parameter validation

**Definition of Done**

* Tests written and passing for all functionality
* Gate types implemented correctly
* Circuit construction API working
* Basic optimizations reducing gate count
* OpenQASM compilation producing valid output
* **ALL circuit builder code consolidated in single file CircuitBuilder.fs**
* Code review completed and approved
* Circuit equivalence validated with test cases

**Labels**
backend, implementation, quantum, circuit


## Metadata
- **Ticket ID:** f6a7b8c9-0d1e-2f3a-4b5c-6d7e8f9a0b1c

---
_Assigned via WorkflowAutomation_
_Full ticket details are included in this file for your reference._
