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
