namespace FSharp.Azure.Quantum.Tests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open Xunit
open FSharp.Azure.Quantum.Core.IonQBackend
open FSharp.Azure.Quantum.Core.Types

module IonQBackendTests =
    
    // ============================================================================
    // TDD CYCLE 1: Gate Type Definitions
    // ============================================================================
    
    [<Fact>]
    let ``IonQGate - SingleQubit gate has gate name and target`` () =
        // Arrange & Act
        let gate = IonQGate.SingleQubit("h", 0)
        
        // Assert
        match gate with
        | IonQGate.SingleQubit(gateName, target) ->
            Assert.Equal("h", gateName)
            Assert.Equal(0, target)
        | _ -> Assert.True(false, "Expected SingleQubit gate")
    
    [<Fact>]
    let ``IonQGate - TwoQubit gate has gate name, control, and target`` () =
        // Arrange & Act
        let gate = IonQGate.TwoQubit("cnot", 0, 1)
        
        // Assert
        match gate with
        | IonQGate.TwoQubit(gateName, control, target) ->
            Assert.Equal("cnot", gateName)
            Assert.Equal(0, control)
            Assert.Equal(1, target)
        | _ -> Assert.True(false, "Expected TwoQubit gate")
    
    [<Fact>]
    let ``IonQGate - SingleQubitRotation has gate name, target, and rotation angle`` () =
        // Arrange & Act
        let gate = IonQGate.SingleQubitRotation("rx", 2, Math.PI / 2.0)
        
        // Assert
        match gate with
        | IonQGate.SingleQubitRotation(gateName, target, rotation) ->
            Assert.Equal("rx", gateName)
            Assert.Equal(2, target)
            Assert.Equal(Math.PI / 2.0, rotation, 6)
        | _ -> Assert.True(false, "Expected SingleQubitRotation gate")
    
    [<Fact>]
    let ``IonQGate - Measure gate has array of target qubits`` () =
        // Arrange & Act
        let gate = IonQGate.Measure([| 0; 1; 2 |])
        
        // Assert
        match gate with
        | IonQGate.Measure(targets) ->
            Assert.Equal(3, targets.Length)
            Assert.Equal<int seq>([| 0; 1; 2 |], targets)
        | _ -> Assert.True(false, "Expected Measure gate")
    
    [<Fact>]
    let ``IonQCircuit - contains qubit count and gate list`` () =
        // Arrange
        let gates = [
            IonQGate.SingleQubit("h", 0)
            IonQGate.TwoQubit("cnot", 0, 1)
            IonQGate.Measure([| 0; 1 |])
        ]
        
        // Act
        let circuit = { Qubits = 2; Circuit = gates }
        
        // Assert
        Assert.Equal(2, circuit.Qubits)
        Assert.Equal(3, circuit.Circuit.Length)
    
    // ============================================================================
    // TDD CYCLE 2: Gate Serialization to JSON
    // ============================================================================
    
    [<Fact>]
    let ``serializeGate - SingleQubit gate serializes to JSON with gate and target`` () =
        // Arrange
        let gate = IonQGate.SingleQubit("h", 0)
        
        // Act
        let json = serializeGate gate
        let jsonDoc = JsonDocument.Parse(json)
        let root = jsonDoc.RootElement
        
        // Assert
        Assert.Equal("h", root.GetProperty("gate").GetString())
        Assert.Equal(0, root.GetProperty("target").GetInt32())
        Assert.False(root.TryGetProperty("control", ref Unchecked.defaultof<JsonElement>))
        Assert.False(root.TryGetProperty("rotation", ref Unchecked.defaultof<JsonElement>))
    
    [<Fact>]
    let ``serializeGate - TwoQubit gate serializes to JSON with gate, control, and target`` () =
        // Arrange
        let gate = IonQGate.TwoQubit("cnot", 0, 1)
        
        // Act
        let json = serializeGate gate
        let jsonDoc = JsonDocument.Parse(json)
        let root = jsonDoc.RootElement
        
        // Assert
        Assert.Equal("cnot", root.GetProperty("gate").GetString())
        Assert.Equal(0, root.GetProperty("control").GetInt32())
        Assert.Equal(1, root.GetProperty("target").GetInt32())
        Assert.False(root.TryGetProperty("rotation", ref Unchecked.defaultof<JsonElement>))
    
    [<Fact>]
    let ``serializeGate - SingleQubitRotation serializes to JSON with gate, target, and rotation`` () =
        // Arrange
        let gate = IonQGate.SingleQubitRotation("rx", 2, Math.PI / 2.0)
        
        // Act
        let json = serializeGate gate
        let jsonDoc = JsonDocument.Parse(json)
        let root = jsonDoc.RootElement
        
        // Assert
        Assert.Equal("rx", root.GetProperty("gate").GetString())
        Assert.Equal(2, root.GetProperty("target").GetInt32())
        Assert.Equal(Math.PI / 2.0, root.GetProperty("rotation").GetDouble(), 6)
        Assert.False(root.TryGetProperty("control", ref Unchecked.defaultof<JsonElement>))
    
    [<Fact>]
    let ``serializeGate - Measure gate serializes to JSON with measure gate and target array`` () =
        // Arrange
        let gate = IonQGate.Measure([| 0; 1; 2 |])
        
        // Act
        let json = serializeGate gate
        let jsonDoc = JsonDocument.Parse(json)
        let root = jsonDoc.RootElement
        
        // Assert
        Assert.Equal("measure", root.GetProperty("gate").GetString())
        let targets = root.GetProperty("target").EnumerateArray() |> Seq.map (fun e -> e.GetInt32()) |> Seq.toArray
        Assert.Equal<int seq>([| 0; 1; 2 |], targets)
    
    // ============================================================================
    // TDD CYCLE 3: Circuit Serialization
    // ============================================================================
    
    [<Fact>]
    let ``serializeCircuit - Bell state circuit serializes correctly`` () =
        // Arrange - Bell state: H on qubit 0, CNOT(0,1), measure both
        let circuit = {
            Qubits = 2
            Circuit = [
                IonQGate.SingleQubit("h", 0)
                IonQGate.TwoQubit("cnot", 0, 1)
                IonQGate.Measure([| 0; 1 |])
            ]
        }
        
        // Act
        let json = serializeCircuit circuit
        let jsonDoc = JsonDocument.Parse(json)
        let root = jsonDoc.RootElement
        
        // Assert
        Assert.Equal(2, root.GetProperty("qubits").GetInt32())
        let gates = root.GetProperty("circuit").EnumerateArray() |> Seq.toList
        Assert.Equal(3, gates.Length)
        
        // Check H gate
        Assert.Equal("h", gates.[0].GetProperty("gate").GetString())
        Assert.Equal(0, gates.[0].GetProperty("target").GetInt32())
        
        // Check CNOT gate
        Assert.Equal("cnot", gates.[1].GetProperty("gate").GetString())
        Assert.Equal(0, gates.[1].GetProperty("control").GetInt32())
        Assert.Equal(1, gates.[1].GetProperty("target").GetInt32())
        
        // Check Measure gate
        Assert.Equal("measure", gates.[2].GetProperty("gate").GetString())
        let measureTargets = gates.[2].GetProperty("target").EnumerateArray() |> Seq.map (fun e -> e.GetInt32()) |> Seq.toArray
        Assert.Equal<int seq>([| 0; 1 |], measureTargets)
    
    [<Fact>]
    let ``serializeCircuit - GHZ state with rotations serializes correctly`` () =
        // Arrange - 3-qubit GHZ state with RX rotation
        let circuit = {
            Qubits = 3
            Circuit = [
                IonQGate.SingleQubit("h", 0)
                IonQGate.SingleQubitRotation("rx", 1, Math.PI / 4.0)
                IonQGate.TwoQubit("cnot", 0, 1)
                IonQGate.TwoQubit("cnot", 1, 2)
                IonQGate.Measure([| 0; 1; 2 |])
            ]
        }
        
        // Act
        let json = serializeCircuit circuit
        let jsonDoc = JsonDocument.Parse(json)
        let root = jsonDoc.RootElement
        
        // Assert
        Assert.Equal(3, root.GetProperty("qubits").GetInt32())
        let gates = root.GetProperty("circuit").EnumerateArray() |> Seq.toList
        Assert.Equal(5, gates.Length)
        
        // Check RX rotation gate
        Assert.Equal("rx", gates.[1].GetProperty("gate").GetString())
        Assert.Equal(1, gates.[1].GetProperty("target").GetInt32())
        Assert.Equal(Math.PI / 4.0, gates.[1].GetProperty("rotation").GetDouble(), 6)
    
    [<Fact>]
    let ``serializeCircuit - Empty circuit with just measurement`` () =
        // Arrange
        let circuit = {
            Qubits = 1
            Circuit = [
                IonQGate.Measure([| 0 |])
            ]
        }
        
        // Act
        let json = serializeCircuit circuit
        let jsonDoc = JsonDocument.Parse(json)
        let root = jsonDoc.RootElement
        
        // Assert
        Assert.Equal(1, root.GetProperty("qubits").GetInt32())
        let gates = root.GetProperty("circuit").EnumerateArray() |> Seq.toList
        Assert.Equal(1, gates.Length)
        Assert.Equal("measure", gates.[0].GetProperty("gate").GetString())
    
    // ============================================================================
    // TDD CYCLE 4: Job Submission
    // ============================================================================
    
    [<Fact>]
    let ``createJobSubmission - Creates correct JobSubmission for IonQ simulator`` () =
        // Arrange
        let circuit = {
            Qubits = 2
            Circuit = [
                IonQGate.SingleQubit("h", 0)
                IonQGate.TwoQubit("cnot", 0, 1)
                IonQGate.Measure([| 0; 1 |])
            ]
        }
        let shots = 1000
        let target = "ionq.simulator"
        
        // Act
        let submission = createJobSubmission circuit shots target
        
        // Assert
        Assert.NotNull(submission.JobId)
        Assert.Equal(target, submission.Target)
        Assert.Equal(CircuitFormat.IonQ_V1, submission.InputDataFormat)
        
        // Verify input data is serialized circuit
        let inputDataStr = submission.InputData :?> string
        let inputDoc = JsonDocument.Parse(inputDataStr)
        Assert.Equal(2, inputDoc.RootElement.GetProperty("qubits").GetInt32())
        
        // Verify input params contain shots
        Assert.True(submission.InputParams.ContainsKey("shots"))
        Assert.Equal(shots, submission.InputParams.["shots"] :?> int)
    
    [<Fact>]
    let ``createJobSubmission - Supports different targets (simulator and hardware)`` () =
        // Arrange
        let circuit = { Qubits = 1; Circuit = [ IonQGate.Measure([| 0 |]) ] }
        
        // Act - Simulator
        let simSubmission = createJobSubmission circuit 100 "ionq.simulator"
        // Act - Hardware (Aria)
        let ariaSubmission = createJobSubmission circuit 100 "ionq.qpu.aria-1"
        
        // Assert
        Assert.Equal("ionq.simulator", simSubmission.Target)
        Assert.Equal("ionq.qpu.aria-1", ariaSubmission.Target)
        Assert.NotEqual<string>(simSubmission.JobId, ariaSubmission.JobId) // Different job IDs
    
    [<Fact>]
    let ``createJobSubmission - Uses correct circuit format IonQ_V1`` () =
        // Arrange
        let circuit = { Qubits = 1; Circuit = [ IonQGate.Measure([| 0 |]) ] }
        
        // Act
        let submission = createJobSubmission circuit 500 "ionq.simulator"
        
        // Assert
        Assert.Equal("ionq.circuit.v1", submission.InputDataFormat.ToFormatString())
