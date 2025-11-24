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
    let circuit = { NumQubits = 10; GateCount = 20; UsedGates = Set.ofList ["H"; "CNOT"] }
    
    // Act
    let result = validateQubitCount IonQHardware circuit
    
    // Assert - 10 qubits is within IonQ Hardware limit of 11
    match result with
    | Ok () -> Assert.True(true)
    | Error _ -> Assert.True(false, "Expected validation to pass but got error")

[<Fact>]
let ``Validate circuit exceeding qubit limit should return error`` () =
    // Arrange - IonQ Hardware has 11 qubit limit
    let circuit = { NumQubits = 15; GateCount = 20; UsedGates = Set.ofList ["H"; "CNOT"] }
    
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
    let circuit = { NumQubits = 5; GateCount = 10; UsedGates = Set.ofList ["H"; "CNOT"; "Rx"] }
    
    // Act
    let result = validateGateSet IonQSimulator circuit
    
    // Assert - All gates are supported
    match result with
    | Ok () -> Assert.True(true)
    | Error err -> Assert.True(false, sprintf "Expected validation to pass but got error: %A" err)

[<Fact>]
let ``Validate circuit with unsupported gates should return errors`` () =
    // Arrange - IonQ does NOT support CZ gate (Rigetti-specific)
    let circuit = { NumQubits = 5; GateCount = 10; UsedGates = Set.ofList ["H"; "CZ"; "Toffoli"] }
    
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
    let circuit = { NumQubits = 5; GateCount = 80; UsedGates = Set.ofList ["H"; "CNOT"] }
    
    // Act
    let result = validateCircuitDepth IonQSimulator circuit
    
    // Assert - 80 gates is within limit of 100
    match result with
    | Ok () -> Assert.True(true)
    | Error err -> Assert.True(false, sprintf "Expected validation to pass but got error: %A" err)
