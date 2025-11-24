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
