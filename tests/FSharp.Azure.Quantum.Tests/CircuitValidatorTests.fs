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
