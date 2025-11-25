# Architecture Issues & Technical Debt

## ✅ RESOLVED: CircuitValidator Extensibility (2025-11-25)

### Problem (Before)
`CircuitValidator.fs` used a **closed discriminated union** for backends, which made it impossible to add new providers without modifying the library.

### Solution (Implemented)
Refactored to use **configurable `BackendConstraints`** with factory functions:

```fsharp
// User-extensible constraint system
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
    let ionqSimulator () : BackendConstraints = { ... }
    let ionqHardware () : BackendConstraints = { ... }
    let rigettiAspenM3 () : BackendConstraints = { ... }
    let create name maxQubits gates ... = { ... }  // User-extensible
```

### Current State (After)
- **QUBO Encoding**: ✅ Provider-agnostic (pure math transformations)
- **CircuitValidator**: ✅ Provider-agnostic with extensible constraints
- **IonQ Backend**: ✅ Integrated pre-flight validation (`submitAndWaitForResultsWithValidationAsync`)
- **Rigetti Backend**: ✅ Integrated pre-flight validation (`validateProgramWithConstraints`)
- **KnownTargets Module**: ✅ Auto-detects constraints from Azure Quantum target strings

### Benefits Achieved
1. ✅ **Extensible**: Users can create custom `BackendConstraints` for any provider
2. ✅ **Early validation**: Catches errors before expensive Azure API calls
3. ✅ **Third-party friendly**: IBM, Google, Amazon Braket can be added without library changes
4. ✅ **Type-safe**: F# type system prevents invalid configurations
5. ✅ **No breaking changes**: Clean API design (no deprecated legacy code)

### Example Usage

```fsharp
// Use built-in constraints for local simulator
let localConstraints = BackendConstraints.localSimulator()
let result = CircuitValidator.validateCircuit localConstraints myCircuit

// Use built-in constraints for cloud backends
let ionqConstraints = BackendConstraints.ionqSimulator()
let rigettiConstraints = BackendConstraints.rigettiAspenM3()

// Create custom constraints for a new provider
let ibmConstraints = BackendConstraints.create
    "IBM Quantum"
    127  // qubits
    ["H"; "X"; "Y"; "Z"; "CX"; "RZ"; "SX"]  // gates
    (Some 1000)  // max depth
    false  // limited connectivity
    [(0,1); (1,2); (2,3)]  // connectivity graph

// Auto-detect from target string (includes local simulator)
let constraints = CircuitValidator.KnownTargets.getConstraints "local"
let azureConstraints = CircuitValidator.KnownTargets.getConstraints "ionq.simulator"
```

### Supported Backend Constraints

| Backend | Factory Function | Max Qubits | Connectivity | Depth Limit |
|---------|-----------------|------------|--------------|-------------|
| Local QAOA Simulator | `BackendConstraints.localSimulator()` | 10 | All-to-all | None |
| IonQ Simulator | `BackendConstraints.ionqSimulator()` | 29 | All-to-all | 100 gates |
| IonQ Hardware (Aria) | `BackendConstraints.ionqHardware()` | 11 | All-to-all | 100 gates |
| Rigetti Aspen-M-3 | `BackendConstraints.rigettiAspenM3()` | 79 | Limited | 50 gates |

---

## 📋 Original Proposed Solution (v1.0) - NOW IMPLEMENTED

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

## 📊 How to Use Validation (v0.5.0-beta)

### IonQ Backend with Auto-Validation:
```fsharp
// Validation happens automatically before submission
let! result = IonQBackend.submitAndWaitForResultsWithValidationAsync
    httpClient
    workspaceUrl
    circuit
    1000  // shots
    "ionq.simulator"
    None  // Auto-detect constraints from target string

// Or provide custom constraints
let customConstraints = BackendConstraints.create "Custom" 10 ["H"; "CNOT"] None true []
let! result = IonQBackend.submitAndWaitForResultsWithValidationAsync
    httpClient workspaceUrl circuit 1000 "ionq.simulator" (Some customConstraints)
```

### Rigetti Backend with Pre-Flight Validation:
```fsharp
// Validate before creating job submission
let validationResult = RigettiBackend.validateProgramWithConstraints
    program
    "rigetti.sim.qvm"
    None  // Auto-detect constraints

match validationResult with
| Ok () ->
    // Submit job...
    let submission = RigettiBackend.createJobSubmission program 1000 "rigetti.sim.qvm" None
    JobLifecycle.submitJobAsync httpClient workspaceUrl submission
| Error (InvalidCircuit errors) ->
    // Handle validation errors before submission
    printfn "Validation failed: %A" errors
```

### Manual Validation for Any Circuit:
```fsharp
let constraints = BackendConstraints.ionqSimulator()
let circuitInfo = {
    NumQubits = 5
    GateCount = 100
    UsedGates = Set.ofList ["H"; "CNOT"; "RX"]
    TwoQubitGates = [(0,1); (1,2)]
}

match CircuitValidator.validateCircuit constraints circuitInfo with
| Ok () -> printfn "Circuit valid!"
| Error errors ->
    let errorMsg = CircuitValidator.formatValidationErrors errors
    printfn "%s" errorMsg
```

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

### v0.5.0-beta (Current - November 2025)
- ✅ Working IonQ and Rigetti backends
- ✅ Configurable backend constraints (IMPLEMENTED)
- ✅ Automatic validation before submission (IMPLEMENTED)
- ✅ Built-in constraint definitions for IonQ and Rigetti (IMPLEMENTED)
- ✅ User-extensible constraint system (IMPLEMENTED)
- ✅ KnownTargets module for auto-detection (IMPLEMENTED)

### v1.0 (Future)
- 🎯 Add built-in constraints for IBM Quantum
- 🎯 Add built-in constraints for Amazon Braket
- 🎯 Add built-in constraints for Google Cirq/Quantum AI
- 🎯 Enhanced connectivity graph validation (routing algorithms)
- 🎯 Circuit optimization suggestions based on constraints

### Future
- Advanced circuit decomposition for unsupported gates
- Multi-provider circuit transpilation
- Automatic qubit mapping for limited connectivity

---

**Date:** 2025-11-25  
**Status:** RESOLVED ✅  
**Implementation:** All proposed features implemented in v0.5.0-beta  
**Test Coverage:** 547 tests passing  
**Breaking Changes:** None (clean API design)
