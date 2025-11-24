module FSharp.Azure.Quantum.Tests.CircuitValidatorTests

open Xunit
open FSharp.Azure.Quantum.Core.CircuitValidator

[<Fact>]
let ``Backend constraint should define IonQ simulator with 29 qubits`` () =
    // Arrange
    let constraints = getConstraints IonQSimulator
    
    // Assert
    Assert.Equal(29, constraints.MaxQubits)
    Assert.Equal("IonQ Simulator", constraints.Name)

[<Fact>]
let ``Backend constraint should define IonQ hardware with 11 qubits`` () =
    // Arrange
    let constraints = getConstraints IonQHardware
    
    // Assert
    Assert.Equal(11, constraints.MaxQubits)
    Assert.Equal("IonQ Hardware", constraints.Name)
    Assert.True(constraints.HasAllToAllConnectivity)

[<Fact>]
let ``Backend constraint should define Rigetti Aspen-M-3 with 79 qubits`` () =
    // Arrange
    let constraints = getConstraints RigettiAspenM3
    
    // Assert
    Assert.Equal(79, constraints.MaxQubits)
    Assert.Equal("Rigetti Aspen-M-3", constraints.Name)
    Assert.False(constraints.HasAllToAllConnectivity)
    Assert.Contains("CZ", constraints.SupportedGates)

[<Fact>]
let ``IonQ backends should support standard gate set`` () =
    // Arrange
    let simConstraints = getConstraints IonQSimulator
    let hwConstraints = getConstraints IonQHardware
    
    // Assert - both IonQ backends support same gate set
    let expectedGates = Set.ofList ["X"; "Y"; "Z"; "H"; "Rx"; "Ry"; "Rz"; "CNOT"; "SWAP"]
    Assert.Equal<Set<string>>(expectedGates, simConstraints.SupportedGates)
    Assert.Equal<Set<string>>(expectedGates, hwConstraints.SupportedGates)

[<Fact>]
let ``Backend constraints should define circuit depth limits`` () =
    // Arrange & Act
    let ionqSim = getConstraints IonQSimulator
    let ionqHw = getConstraints IonQHardware
    let rigetti = getConstraints RigettiAspenM3
    
    // Assert - IonQ has 100 gate depth limit
    Assert.Equal(Some 100, ionqSim.MaxCircuitDepth)
    Assert.Equal(Some 100, ionqHw.MaxCircuitDepth)
    
    // Assert - Rigetti has 50 gate depth limit
    Assert.Equal(Some 50, rigetti.MaxCircuitDepth)

// ============================================================================
// Day 2: Validation Logic Tests
// ============================================================================

[<Fact>]
let ``Validate circuit with qubit count within backend limits should pass`` () =
    // Arrange
    let circuit = { NumQubits = 10; GateCount = 20; UsedGates = Set.ofList ["H"; "CNOT"]; TwoQubitGates = [] }
    
    // Act
    let result = validateQubitCount IonQHardware circuit
    
    // Assert - 10 qubits is within IonQ Hardware limit of 11
    match result with
    | Ok () -> Assert.True(true)
    | Error _ -> Assert.True(false, "Expected validation to pass but got error")

[<Fact>]
let ``Validate circuit exceeding qubit limit should return error`` () =
    // Arrange - IonQ Hardware has 11 qubit limit
    let circuit = { NumQubits = 15; GateCount = 20; UsedGates = Set.ofList ["H"; "CNOT"]; TwoQubitGates = [] }
    
    // Act
    let result = validateQubitCount IonQHardware circuit
    
    // Assert - Should return QubitCountExceeded error
    match result with
    | Ok () -> Assert.True(false, "Expected validation to fail but it passed")
    | Error (QubitCountExceeded(requested, limit, backend)) ->
        Assert.Equal(15, requested)
        Assert.Equal(11, limit)
        Assert.Equal("IonQ Hardware", backend)
    | Error other -> Assert.True(false, sprintf "Expected QubitCountExceeded but got %A" other)

[<Fact>]
let ``Validate circuit with supported gates should pass`` () =
    // Arrange - IonQ supports H, X, Y, Z, CNOT, SWAP, Rx, Ry, Rz
    let circuit = { NumQubits = 5; GateCount = 10; UsedGates = Set.ofList ["H"; "CNOT"; "Rx"]; TwoQubitGates = [] }
    
    // Act
    let result = validateGateSet IonQSimulator circuit
    
    // Assert - All gates are supported
    match result with
    | Ok () -> Assert.True(true)
    | Error err -> Assert.True(false, sprintf "Expected validation to pass but got error: %A" err)

[<Fact>]
let ``Validate circuit with unsupported gates should return errors`` () =
    // Arrange - IonQ does NOT support CZ gate (Rigetti-specific)
    let circuit = { NumQubits = 5; GateCount = 10; UsedGates = Set.ofList ["H"; "CZ"; "Toffoli"]; TwoQubitGates = [] }
    
    // Act
    let result = validateGateSet IonQSimulator circuit
    
    // Assert - Should return UnsupportedGate errors for CZ and Toffoli
    match result with
    | Ok () -> Assert.True(false, "Expected validation to fail but it passed")
    | Error errors ->
        Assert.Equal(2, errors.Length)
        // Verify error details
        errors |> List.iter (fun err ->
            match err with
            | UnsupportedGate(gate, backend, _) ->
                Assert.True(gate = "CZ" || gate = "Toffoli", sprintf "Unexpected unsupported gate: %s" gate)
                Assert.Equal("IonQ Simulator", backend)
            | _ -> Assert.True(false, sprintf "Expected UnsupportedGate but got %A" err)
        )

[<Fact>]
let ``Validate circuit within depth limit should pass`` () =
    // Arrange - IonQ has depth limit of 100, circuit has 80 gates
    let circuit = { NumQubits = 5; GateCount = 80; UsedGates = Set.ofList ["H"; "CNOT"]; TwoQubitGates = [] }
    
    // Act
    let result = validateCircuitDepth IonQSimulator circuit
    
    // Assert - 80 gates is within limit of 100
    match result with
    | Ok () -> Assert.True(true)
    | Error err -> Assert.True(false, sprintf "Expected validation to pass but got error: %A" err)

[<Fact>]
let ``Validate circuit exceeding depth limit should return error`` () =
    // Arrange - Rigetti has depth limit of 50, circuit has 75 gates
    let circuit = { NumQubits = 10; GateCount = 75; UsedGates = Set.ofList ["H"; "CZ"]; TwoQubitGates = [] }
    
    // Act
    let result = validateCircuitDepth RigettiAspenM3 circuit
    
    // Assert - Should return CircuitDepthExceeded error
    match result with
    | Ok () -> Assert.True(false, "Expected validation to fail but it passed")
    | Error (CircuitDepthExceeded(depth, limit, backend)) ->
        Assert.Equal(75, depth)
        Assert.Equal(50, limit)
        Assert.Equal("Rigetti Aspen-M-3", backend)
    | Error other -> Assert.True(false, sprintf "Expected CircuitDepthExceeded but got %A" other)

[<Fact>]
let ``Validate IonQ all-to-all connectivity should always pass`` () =
    // Arrange - IonQ has all-to-all connectivity, any qubit pair is valid
    let circuit = { 
        NumQubits = 5
        GateCount = 10
        UsedGates = Set.ofList ["H"; "CNOT"]
        TwoQubitGates = [(0, 4); (2, 3); (1, 4)]  // Any pairs should work
    }
    
    // Act
    let result = validateConnectivity IonQSimulator circuit
    
    // Assert - Should pass regardless of qubit pairs
    match result with
    | Ok () -> Assert.True(true)
    | Error err -> Assert.True(false, sprintf "Expected validation to pass but got error: %A" err)

[<Fact>]
let ``Validate Rigetti limited connectivity should reject invalid qubit pairs`` () =
    // Arrange - Rigetti Aspen-M-3 has limited connectivity
    // For this test, we'll define a simple connectivity: (0,1), (1,2), (2,3)
    // A circuit trying to connect (0,3) should fail
    let circuit = { 
        NumQubits = 5
        GateCount = 10
        UsedGates = Set.ofList ["H"; "CZ"]
        TwoQubitGates = [(0, 1); (0, 3); (2, 4)]  // (0,1) valid, (0,3) and (2,4) invalid
    }
    
    // Act
    let result = validateConnectivity RigettiAspenM3 circuit
    
    // Assert - Should return ConnectivityViolation errors
    match result with
    | Ok () -> Assert.True(false, "Expected validation to fail but it passed")
    | Error errors ->
        Assert.Equal(2, errors.Length)
        // Verify we get ConnectivityViolation for invalid pairs
        errors |> List.iter (fun err ->
            match err with
            | ConnectivityViolation(q1, q2, backend) ->
                Assert.True((q1 = 0 && q2 = 3) || (q1 = 2 && q2 = 4), 
                           sprintf "Expected invalid pairs (0,3) or (2,4) but got (%d,%d)" q1 q2)
                Assert.Equal("Rigetti Aspen-M-3", backend)
            | _ -> Assert.True(false, sprintf "Expected ConnectivityViolation but got %A" err)
        )

[<Fact>]
let ``Validate empty circuit should pass all validations`` () =
    // Arrange - Empty circuit with no qubits, no gates
    let circuit = { 
        NumQubits = 0
        GateCount = 0
        UsedGates = Set.empty
        TwoQubitGates = []
    }
    
    // Act - Run all validation functions
    let qubitResult = validateQubitCount IonQSimulator circuit
    let gateSetResult = validateGateSet IonQSimulator circuit
    let depthResult = validateCircuitDepth IonQSimulator circuit
    let connectivityResult = validateConnectivity IonQSimulator circuit
    
    // Assert - All validations should pass for empty circuit
    match qubitResult with
    | Ok () -> Assert.True(true)
    | Error err -> Assert.True(false, sprintf "Qubit validation failed: %A" err)
    
    match gateSetResult with
    | Ok () -> Assert.True(true)
    | Error err -> Assert.True(false, sprintf "Gate set validation failed: %A" err)
    
    match depthResult with
    | Ok () -> Assert.True(true)
    | Error err -> Assert.True(false, sprintf "Depth validation failed: %A" err)
    
    match connectivityResult with
    | Ok () -> Assert.True(true)
    | Error err -> Assert.True(false, sprintf "Connectivity validation failed: %A" err)

[<Fact>]
let ``Validate circuit with multiple violations should catch all issues`` () =
    // Arrange - Circuit that violates multiple constraints:
    // - Exceeds IonQ Hardware qubit limit (12 > 11)
    // - Uses unsupported gates (CZ, Toffoli)
    // - Exceeds depth limit (150 > 100)
    let circuit = { 
        NumQubits = 12
        GateCount = 150
        UsedGates = Set.ofList ["H"; "CNOT"; "CZ"; "Toffoli"]
        TwoQubitGates = []
    }
    
    // Act - Run all validation functions
    let qubitResult = validateQubitCount IonQHardware circuit
    let gateSetResult = validateGateSet IonQHardware circuit
    let depthResult = validateCircuitDepth IonQHardware circuit
    
    // Assert - All three validations should fail with specific errors
    match qubitResult with
    | Error (QubitCountExceeded(12, 11, "IonQ Hardware")) -> Assert.True(true)
    | _ -> Assert.True(false, "Expected QubitCountExceeded error")
    
    match gateSetResult with
    | Error errors ->
        Assert.Equal(2, errors.Length)
        // Both CZ and Toffoli should be flagged as unsupported
        errors |> List.iter (fun err ->
            match err with
            | UnsupportedGate(gate, "IonQ Hardware", _) ->
                Assert.True(gate = "CZ" || gate = "Toffoli", sprintf "Unexpected gate: %s" gate)
            | _ -> Assert.True(false, "Expected UnsupportedGate error")
        )
    | _ -> Assert.True(false, "Expected gate set errors")
    
    match depthResult with
    | Error (CircuitDepthExceeded(150, 100, "IonQ Hardware")) -> Assert.True(true)
    | _ -> Assert.True(false, "Expected CircuitDepthExceeded error")

// ============================================================================
// Day 3: Integration & Error Messages Tests
// ============================================================================

[<Fact>]
let ``Full validation should pass for valid circuit`` () =
    // Arrange - Valid circuit that passes all validations
    let circuit = { 
        NumQubits = 5
        GateCount = 30
        UsedGates = Set.ofList ["H"; "CNOT"; "Rx"]
        TwoQubitGates = [(0, 1); (1, 2)]
    }
    
    // Act - Run full validation
    let result = validateCircuit IonQSimulator circuit
    
    // Assert - Should pass all validations
    match result with
    | Ok () -> Assert.True(true)
    | Error errors -> 
        Assert.True(false, sprintf "Expected validation to pass but got errors: %A" errors)

[<Fact>]
let ``Full validation should collect all errors for invalid circuit`` () =
    // Arrange - Invalid circuit with multiple violations
    let circuit = { 
        NumQubits = 100  // Exceeds IonQ Simulator limit of 29
        GateCount = 200  // Exceeds depth limit of 100
        UsedGates = Set.ofList ["H"; "CZ"; "Toffoli"]  // Contains unsupported gates
        TwoQubitGates = []
    }
    
    // Act - Run full validation
    let result = validateCircuit IonQSimulator circuit
    
    // Assert - Should collect all validation errors
    match result with
    | Ok () -> Assert.True(false, "Expected validation to fail but it passed")
    | Error errors ->
        Assert.True(errors.Length >= 3, sprintf "Expected at least 3 errors but got %d" errors.Length)
        
        // Check that we have qubit count error
        let hasQubitError = 
            errors |> List.exists (fun err ->
                match err with
                | QubitCountExceeded _ -> true
                | _ -> false)
        Assert.True(hasQubitError, "Expected QubitCountExceeded error")
        
        // Check that we have depth error
        let hasDepthError = 
            errors |> List.exists (fun err ->
                match err with
                | CircuitDepthExceeded _ -> true
                | _ -> false)
        Assert.True(hasDepthError, "Expected CircuitDepthExceeded error")
        
        // Check that we have gate errors
        let hasGateErrors = 
            errors |> List.exists (fun err ->
                match err with
                | UnsupportedGate _ -> true
                | _ -> false)
        Assert.True(hasGateErrors, "Expected UnsupportedGate errors")

[<Fact>]
let ``Format validation error should provide clear actionable message`` () =
    // Arrange - Various error types
    let qubitError = QubitCountExceeded(15, 11, "IonQ Hardware")
    let gateError = UnsupportedGate("CZ", "IonQ Simulator", Set.ofList ["H"; "CNOT"; "X"])
    let depthError = CircuitDepthExceeded(150, 100, "IonQ Hardware")
    let connectivityError = ConnectivityViolation(0, 5, "Rigetti Aspen-M-3")
    
    // Act - Format each error
    let qubitMsg = formatValidationError qubitError
    let gateMsg = formatValidationError gateError
    let depthMsg = formatValidationError depthError
    let connectivityMsg = formatValidationError connectivityError
    
    // Assert - Messages are clear and actionable
    Assert.Contains("15 qubits", qubitMsg)
    Assert.Contains("11", qubitMsg)
    Assert.Contains("IonQ Hardware", qubitMsg)
    
    Assert.Contains("CZ", gateMsg)
    Assert.Contains("not supported", gateMsg)
    Assert.Contains("IonQ Simulator", gateMsg)
    
    Assert.Contains("150", depthMsg)
    Assert.Contains("100", depthMsg)
    Assert.Contains("depth", depthMsg.ToLower())
    
    Assert.Contains("0", connectivityMsg)
    Assert.Contains("5", connectivityMsg)
    Assert.Contains("Rigetti", connectivityMsg)

[<Fact>]
let ``Format validation errors should create summary message`` () =
    // Arrange - Multiple errors
    let errors = [
        QubitCountExceeded(30, 29, "IonQ Simulator")
        UnsupportedGate("Toffoli", "IonQ Simulator", Set.empty)
        CircuitDepthExceeded(120, 100, "IonQ Simulator")
    ]
    
    // Act - Format all errors as summary
    let summary = formatValidationErrors errors
    
    // Assert - Summary contains all error info
    Assert.Contains("3 validation error", summary)
    Assert.Contains("qubit", summary.ToLower())
    Assert.Contains("Toffoli", summary)
    Assert.Contains("depth", summary.ToLower())
