namespace FSharp.Azure.Quantum.Tests

open Xunit
open FSharp.Azure.Quantum
open FSharp.Azure.Quantum.CircuitBuilder

/// Tests for QirEmitter — QIR base-profile LLVM IR emission.
module QirEmitterTests =

    let private emitOrFail c =
        match QirEmitter.emit c with
        | Ok ir -> ir
        | Error e -> failwithf "emit failed: %s" e

    [<Fact>]
    let ``Bell circuit emits well-formed base-profile QIR`` () =
        let ir =
            empty 2
            |> addGate (H 0)
            |> addGate (CNOT(0, 1))
            |> addMeasurement 0
            |> addMeasurement 1
            |> emitOrFail
        Assert.Contains("%Qubit = type opaque", ir)
        Assert.Contains("define void @main() #0", ir)
        Assert.Contains("__quantum__qis__h__body", ir)
        Assert.Contains("__quantum__qis__cnot__body", ir)
        Assert.Contains("__quantum__qis__mz__body", ir)
        Assert.Contains("__quantum__rt__result_record_output", ir)
        Assert.Contains("\"entry_point\"", ir)
        Assert.Contains("\"required_num_qubits\"=\"2\"", ir)
        Assert.Contains("\"required_num_results\"=\"2\"", ir)

    [<Fact>]
    let ``rotation gates emit rz with a hex double literal`` () =
        let ir = empty 1 |> addGate (RZ(0, 0.7853981633974483)) |> emitOrFail
        Assert.Contains("__quantum__qis__rz__body(double 0x", ir)

    [<Fact>]
    let ``only used intrinsics are declared`` () =
        let ir = empty 1 |> addGate (X 0) |> emitOrFail
        Assert.Contains("declare void @__quantum__qis__x__body", ir)
        Assert.DoesNotContain("__quantum__qis__swap__body", ir)
        Assert.DoesNotContain("__quantum__qis__cnot__body", ir)

    [<Fact>]
    let ``gate without a base-profile intrinsic returns Error`` () =
        match QirEmitter.emit (empty 1 |> addGate (U3(0, 0.1, 0.2, 0.3))) with
        | Ok _ -> Assert.True(false, "expected Error for U3")
        | Error e -> Assert.Contains("U3", e)
