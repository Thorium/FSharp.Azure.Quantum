# Architecture Issues & Technical Debt

## 🔴 CRITICAL: CircuitValidator Not Extensible

### Problem
`CircuitValidator.fs` uses a **closed discriminated union** for backends:

```fsharp
type Backend =
    | IonQSimulator
    | IonQHardware
    | RigettiAspenM3
```

**Issues:**
1. ❌ Cannot add new providers without modifying core library code
2. ❌ Third-party extensions impossible
3. ❌ Hardcoded constraints for each backend
4. ❌ NOT integrated into actual submission workflow (only used in tests!)

### Current State
- **QUBO Encoding**: ✅ Provider-agnostic (pure math transformations)
- **CircuitValidator**: ❌ Provider-specific, closed enum
- **IonQ/Rigetti Backends**: ⚠️ Do NOT call CircuitValidator before submission

### Impact
- Users can submit invalid circuits and only discover errors after costly Azure API calls
- Adding support for new providers (IBM, Google, Amazon Braket) requires forking the library
- No runtime validation before job submission

---

## 📋 Recommended Solution (v1.0)

### 1. Make Backend Constraints Configurable

**Replace closed enum with configuration:**

```fsharp
// OLD (current - closed)
type Backend =
    | IonQSimulator
    | IonQHardware
    | RigettiAspenM3

// NEW (proposed - open)
type BackendConstraints = {
    Name: string
    MaxQubits: int
    SupportedGates: Set<string>
    MaxCircuitDepth: int option
    HasAllToAllConnectivity: bool
    ConnectedPairs: Set<int * int>
}

// Factory functions for built-in providers
module BackendConstraints =
    let ionqSimulator () = { ... }
    let ionqHardware () = { ... }
    let rigettiAspenM3 () = { ... }
    
    // Users can create custom constraints
    let custom name maxQubits gates connectivity = { ... }
```

### 2. Integrate Validation into Submission Path

**Add validation hooks in backends:**

```fsharp
// IonQBackend.fs
let submitAndWaitForResultsAsync
    (httpClient: HttpClient)
    (workspaceUrl: string)
    (circuit: IonQCircuit)
    (shots: int)
    (target: string)
    (constraints: BackendConstraints option)  // <-- NEW
    : Async<Result<Map<string, int>, QuantumError>> =
    async {
        // Validate before submission (if constraints provided)
        match constraints with
        | Some c ->
            let circuitInfo = extractCircuitInfo circuit
            match CircuitValidator.validateCircuit c circuitInfo with
            | Error errors -> 
                return Error (QuantumError.InvalidCircuit errors)
            | Ok () -> ()
        | None -> ()
        
        // Proceed with submission...
    }
```

### 3. Provide Default Constraints per Target

```fsharp
// Map Azure Quantum target strings to constraints
module KnownTargets =
    let getConstraints (target: string) : BackendConstraints option =
        match target with
        | "ionq.simulator" -> Some (BackendConstraints.ionqSimulator())
        | "ionq.qpu.aria-1" -> Some (BackendConstraints.ionqHardware())
        | "rigetti.sim.qvm" -> Some (BackendConstraints.rigettiAspenM3())
        | _ -> None  // Unknown target - skip validation
```

---

## 🎯 Benefits of Solution

1. ✅ **Extensible**: Users can add custom backend constraints without forking
2. ✅ **Early validation**: Catch errors before expensive API calls
3. ✅ **Third-party friendly**: IBM, Google, Amazon Braket providers can be added
4. ✅ **Backward compatible**: Optional parameter, existing code works unchanged
5. ✅ **Type-safe**: F# type system prevents invalid configurations

---

## 📊 Current Workarounds (v0.5.0-beta)

Until v1.0 fixes are implemented:

### For Users:
- Manually validate circuits using `CircuitValidator` module before submission
- Be aware of backend limits (IonQ: 29 qubits, Rigetti: 79 qubits)
- Handle `QuantumError.InvalidCircuit` from Azure API responses

### For Library Developers:
- Document backend constraints in README
- Add examples showing manual validation
- Consider validation integration as high-priority v1.0 feature

---

## 🔍 Related Components

**Provider-Agnostic (Good!):**
- ✅ `QuboEncoding.fs` - Pure mathematical transformations
- ✅ `Authentication.fs` - Works with any Azure service
- ✅ `JobLifecycle.fs` - Generic job submission/polling
- ✅ `Client.fs` - REST API wrapper (not provider-specific)

**Provider-Specific (Needs Review):**
- ⚠️ `CircuitValidator.fs` - Hardcoded backend enum
- ⚠️ `IonQBackend.fs` - IonQ-specific circuit format
- ⚠️ `RigettiBackend.fs` - Quil-specific assembly

**Verdict:** Backend modules SHOULD be provider-specific (they handle different formats).
CircuitValidator should be configurable, not hardcoded.

---

## 📅 Roadmap

### v0.5.0-beta (Current)
- ✅ Working IonQ and Rigetti backends
- ⚠️ Manual circuit validation required
- ⚠️ No automatic pre-submission validation

### v1.0 (Target)
- 🎯 Configurable backend constraints
- 🎯 Automatic validation before submission
- 🎯 Built-in constraint definitions for major providers
- 🎯 User-extensible constraint system

### Future
- Support for IBM Qiskit backends
- Support for Amazon Braket
- Support for Google Cirq/Quantum AI

---

**Date:** 2025-11-25  
**Status:** Documented for v1.0 planning  
**Severity:** Medium (workarounds available, but affects extensibility)
